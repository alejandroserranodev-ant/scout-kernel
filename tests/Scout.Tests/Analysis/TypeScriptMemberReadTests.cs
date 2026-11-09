using Scout.Analysis.TypeScript;
using Scout.Core.Analysis;
using Scout.Core.Compilation;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class TypeScriptMemberReadTests
{
    private const string Fixture = "MinimalReactTextRepository";

    [Fact]
    public async Task AnalyzeObservesWhichFieldsADeclarationReads()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        var hero = Find(result, "src/ModelHero.tsx::ModelHero");
        foreach (var field in new[] { "Model.version", "Model.tagline" })
        {
            var property = Find(result, $"src/ModelHero.tsx::{field}");
            Assert.Contains(
                result.Relationships,
                relationship => relationship.Kind == KnownRelationshipKinds.References &&
                    relationship.SourceId == hero.Id &&
                    relationship.TargetId == property.Id);
        }
    }

    [Fact]
    public async Task AnalyzeDoesNotTreatADeclarationNameAsAUse()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        var version = Find(result, "src/ModelHero.tsx::Model.version");
        var readers = result.Relationships
            .Where(relationship => relationship.TargetId == version.Id && relationship.Kind != KnownRelationshipKinds.Defines)
            .Select(relationship => relationship.SourceId)
            .ToArray();
        Assert.Equal([Find(result, "src/ModelHero.tsx::ModelHero").Id], readers);
    }

    [Fact]
    public async Task RetrievalNamesTheDeclarationsThatReadASymbol()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        var compiled = await new ScoutCompiler([new TypeScriptLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, Path.Combine(repository.WorkspacePath, "read.scout")))
            .ConfigureAwait(true);
        using var database = SqliteRetrievalDatabase.Open(
            Path.Combine(compiled.Kernel.OutputPath, "retrieval", "index.db"),
            new LexicalEmbeddingModel());

        var version = database.Search("version", limit: 5, kind: KnownCardKinds.Symbol)
            .Select(hit => hit.Card)
            .First(card => card.Name == "version");
        Assert.Contains("ModelHero", version.Related, StringComparer.Ordinal);

        // Asking for the reader's own name still answers with the reader first.
        Assert.Equal("ModelHero", database.Search("ModelHero", limit: 3, kind: KnownCardKinds.Symbol)[0].Card.Name);
    }

    [Fact]
    public async Task RetrievalNamesWhatADeclarationReads()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        using var database = await OpenIndexAsync(repository).ConfigureAwait(true);

        var hero = database.Search("ModelHero", limit: 3, kind: KnownCardKinds.Symbol)[0].Card;

        Assert.Equal("ModelHero", hero.Name);
        Assert.Contains("version", hero.Related, StringComparer.Ordinal);
        Assert.Contains("tagline", hero.Related, StringComparer.Ordinal);
    }

    [Fact]
    public async Task RetrievalProfilesEachFileByWhatItDeclaresReadsAndShows()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        using var database = await OpenIndexAsync(repository).ConfigureAwait(true);

        var hero = Assert.Single(
            database.Search("ModelHero version tagline", limit: 5, kind: KnownCardKinds.File),
            hit => hit.Card.RelativePath == "src/ModelHero.tsx").Card;
        Assert.Equal("ModelHero.tsx", hero.Name);
        Assert.Contains("ModelHero", hero.Related, StringComparer.Ordinal);
        Assert.Contains("version", hero.Related, StringComparer.Ordinal);

        var form = database.Search("Nombre de la empresa SupplierForm", limit: 3, kind: KnownCardKinds.File)[0].Card;
        Assert.Equal("src/SupplierForm.tsx", form.RelativePath);
        Assert.Contains("Nombre de la empresa", form.Related, StringComparer.Ordinal);
        Assert.Contains(form.Facets, facet => facet.Name == "texts" && int.Parse(facet.Value, System.Globalization.CultureInfo.InvariantCulture) > 0);
    }

    private static async Task<SqliteRetrievalDatabase> OpenIndexAsync(TemporaryRepository repository)
    {
        var compiled = await new ScoutCompiler([new TypeScriptLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, Path.Combine(repository.WorkspacePath, "profile.scout")))
            .ConfigureAwait(false);
        return SqliteRetrievalDatabase.Open(
            Path.Combine(compiled.Kernel.OutputPath, "retrieval", "index.db"),
            new LexicalEmbeddingModel());
    }

    private static async Task<LanguageAnalysisResult> AnalyzeAsync()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        var analyzer = new TypeScriptLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact Find(LanguageAnalysisResult result, string qualifiedName) =>
        Assert.Single(result.Symbols, symbol => symbol.QualifiedName == qualifiedName);
}

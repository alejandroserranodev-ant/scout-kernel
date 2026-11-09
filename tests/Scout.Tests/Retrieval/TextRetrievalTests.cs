using Scout.Analysis.TypeScript;
using Scout.Core.Compilation;
using Scout.Core.Ir;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class TextRetrievalTests
{
    [Fact]
    public async Task QueryFindsTheDeclarationThatHoldsTheWordsOfTheQuestion()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var result = await CompileAsync(repository).ConfigureAwait(true);
        using var database = Open(result);

        var hit = database.Search("solicitar informacion", limit: 3, kind: KnownCardKinds.Text)[0];

        Assert.Equal(KnownCardKinds.Text, hit.Card.Kind);
        Assert.Equal("Solicitar información", hit.Card.Name);
        Assert.Equal("src/ModelPanel.tsx", hit.Card.RelativePath);
        Assert.Contains("ModelPanel", hit.Card.Related, StringComparer.Ordinal);
        Assert.Contains("in 'ModelPanel'", hit.Card.Summary, StringComparison.Ordinal);
        Assert.Contains(hit.Card.Facets, facet => facet is { Name: "line", Value: "6" });
    }

    [Fact]
    public async Task QueryIgnoresAccentsWhenMatchingWording()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var result = await CompileAsync(repository).ConfigureAwait(true);
        using var database = Open(result);

        var hit = database.Search("ingresa tu nombre", limit: 3, kind: KnownCardKinds.Text)[0];

        Assert.Equal("Ingresá tu nombre para enviar la consulta.", hit.Card.Name);
        Assert.Equal("src/SupplierForm.tsx", hit.Card.RelativePath);
    }

    [Fact]
    public async Task QueryForADeclarationIsAnsweredByTheDeclarationBeforeItsWording()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var result = await CompileAsync(repository).ConfigureAwait(true);
        using var database = Open(result);

        var hits = database.Search("ModelPanel", limit: 10);

        Assert.Equal(KnownCardKinds.Symbol, hits[0].Card.Kind);
        Assert.Equal("ModelPanel", hits[0].Card.Name);
    }

    [Fact]
    public async Task WordingIsNeverANavigationTargetOrAContract()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var result = await CompileAsync(repository).ConfigureAwait(true);
        using var database = Open(result);

        Assert.DoesNotContain(
            database.Search("Solicitar informacion", limit: 20, kind: KnownCardKinds.Symbol),
            hit => hit.Card.Name == "Solicitar información");
        Assert.DoesNotContain(
            database.Search("Solicitar informacion", limit: 20, kind: KnownCardKinds.Contract),
            hit => hit.Card.Name == "Solicitar información");
    }

    [Fact]
    public async Task WordingDoesNotChangeWhatTheKernelCountsAsDeclared()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var result = await CompileAsync(repository).ConfigureAwait(true);

        var declared = result.Structural.Relationships.Count(relationship =>
            relationship.Kind == KnownRelationshipKinds.Defines &&
            result.Structural.SourceFiles.Any(file => file.Id == relationship.SourceId));
        var module = Assert.Single(result.Intelligence.Modules);
        Assert.Equal(declared, module.SymbolCount);

        var texts = result.Structural.Symbols.Count(symbol => symbol.SymbolKind == KnownSymbolKinds.TextLiteral);
        Assert.True(texts > 0);
        var kernelDocument = await File.ReadAllTextAsync(Path.Combine(result.Kernel.OutputPath, "KERNEL.md")).ConfigureAwait(true);
        Assert.Contains($", {texts} text literals", kernelDocument, StringComparison.Ordinal);
        Assert.Contains($"{result.Structural.Symbols.Count - texts} symbols", kernelDocument, StringComparison.Ordinal);
    }

    private static async Task<ScoutCompilationResult> CompileAsync(TemporaryRepository repository)
    {
        var output = Path.Combine(repository.WorkspacePath, "text.scout");
        return await new ScoutCompiler([new TypeScriptLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, output))
            .ConfigureAwait(false);
    }

    private static SqliteRetrievalDatabase Open(ScoutCompilationResult result) =>
        SqliteRetrievalDatabase.Open(
            Path.Combine(result.Kernel.OutputPath, "retrieval", "index.db"),
            new LexicalEmbeddingModel());
}

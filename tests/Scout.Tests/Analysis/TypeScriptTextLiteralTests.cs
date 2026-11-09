using System.Text;
using Scout.Analysis.TypeScript;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class TypeScriptTextLiteralTests
{
    private const string Fixture = "MinimalReactTextRepository";

    [Fact]
    public async Task AnalyzeObservesTheWordingOfJsxAttributesAndStrings()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Succeeded, result.Analyzer.Status);
        Assert.Empty(result.Diagnostics);

        var observed = TextsOf(result).ToDictionary(text => text.Name, text => text.NativeKind);
        string[] expected =
        [
            "Abrir la ficha del modelo",
            "Calcular",
            "Ej: ElectroMotors S.A.",
            "Explorar el modelo",
            "Hola, quiero más información sobre el",
            "Ingresa el nombre de la empresa.",
            "Ingresá tu nombre para enviar la consulta.",
            "La Paz",
            "Nombre de la empresa",
            "Portada del modelo",
            "Seal",
            "Solicitar información",
            "Ver detalles"
        ];
        Assert.Equal(expected.Order(StringComparer.Ordinal), observed.Keys.Order(StringComparer.Ordinal));

        Assert.Equal("jsx-text", observed["Solicitar información"]);
        Assert.Equal("jsx-text", observed["Calcular"]);
        Assert.Equal("jsx-attribute", observed["Explorar el modelo"]);
        Assert.Equal("jsx-attribute", observed["Abrir la ficha del modelo"]);
        Assert.Equal("string", observed["Ingresá tu nombre para enviar la consulta."]);
        Assert.Equal("string", observed["Ver detalles"]);
    }

    [Fact]
    public async Task AnalyzeKeepsOnlyTheFixedPartsOfATemplate()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        var message = Assert.Single(TextsOf(result), text => text.Name.StartsWith("Hola,", StringComparison.Ordinal));
        Assert.Equal("Hola, quiero más información sobre el", message.Name);
    }

    [Fact]
    public async Task AnalyzeDoesNotReportWiringAsWording()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        string[] wiring =
        [
            "wa.me", "use client", "model-panel", "arena-size-lg", "company name", "Not shown",
            "cover photo", "display: flex", "submit", "idle", "companyName", "./ModelPanel", "ph-bold"
        ];
        var names = TextsOf(result).Select(text => text.Name).ToArray();
        foreach (var fragment in wiring)
        {
            Assert.DoesNotContain(names, name => name.Contains(fragment, StringComparison.Ordinal));
        }

        // Too short to be wording: three characters or fewer.
        Assert.DoesNotContain("ok", names);
        Assert.DoesNotContain("Sí", names);
        Assert.DoesNotContain("BYD", names);
    }

    [Fact]
    public async Task AnalyzeLinksEachTextToTheDeclarationThatHoldsIt()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        Assert.Equal("ModelPanel", HolderOf(result, "Solicitar información").Name);
        Assert.Equal("ModelPanel", HolderOf(result, "Hola, quiero más información sobre el").Name);
        Assert.Equal("SupplierForm", HolderOf(result, "Ingresa el nombre de la empresa.").Name);
        Assert.Equal("SupplierForm", HolderOf(result, "Nombre de la empresa").Name);
        Assert.Equal("EMPTY", HolderOf(result, "La Paz").Name);

        // A member owns its own text, not the class that declares it.
        Assert.Equal("missingName", HolderOf(result, "Ingresá tu nombre para enviar la consulta.").Name);
    }

    [Fact]
    public async Task AnalyzeLocatesJsxTextAtItsFirstWordInsteadOfItsSurroundingWhitespace()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        var text = Assert.Single(TextsOf(result), candidate => candidate.Name == "Solicitar información");
        var location = Assert.Single(text.DeclarationLocations);
        Assert.Equal("src/ModelPanel.tsx", location.RelativePath);
        Assert.Equal(6, location.StartLine);
        Assert.Equal(9, location.StartColumn);
        Assert.Equal(6, location.EndLine);
    }

    [Fact]
    public async Task AnalyzeDoesNotMakeWordingADeclarationOrAConsumer()
    {
        var result = await AnalyzeAsync().ConfigureAwait(true);

        var textIds = TextsOf(result).Select(text => text.Id).ToHashSet();
        Assert.NotEmpty(textIds);

        // Contracts are built from consumption edges and module ownership from `defines`; wording joins neither.
        Assert.DoesNotContain(
            result.Relationships,
            relationship => textIds.Contains(relationship.TargetId) && relationship.Kind != KnownRelationshipKinds.Contains);
        Assert.DoesNotContain(result.Relationships, relationship => textIds.Contains(relationship.SourceId));
        Assert.All(
            TextsOf(result),
            text => Assert.Equal(KnownVisibilities.Unknown, text.Visibility));
    }

    [Fact]
    public async Task AnalyzeProducesTheSameWordingEveryTime()
    {
        var first = await AnalyzeAsync().ConfigureAwait(true);
        var second = await AnalyzeAsync().ConfigureAwait(true);

        Assert.Equal(
            TextsOf(first).Select(text => text.Id.Value),
            TextsOf(second).Select(text => text.Id.Value));
    }

    [Fact]
    public async Task AnalyzeBoundsTheWordingReportedForOneFileAndSaysSo()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        var source = new StringBuilder("export const messages = [\n");
        for (var index = 0; index < 350; index++)
        {
            source.Append("  'Mensaje de prueba número ").Append(index).Append(" para el límite',\n");
        }

        source.Append("];\n");
        repository.WriteFile("src/Messages.ts", source.ToString());

        var result = await AnalyzeAsync(repository).ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Partial, result.Analyzer.Status);
        var limit = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "TS_TEXT_LITERAL_LIMIT");
        Assert.Contains("src/Messages.ts", limit.Message, StringComparison.Ordinal);
        Assert.Equal(
            300,
            TextsOf(result).Count(text => text.DeclarationLocations[0].RelativePath == "src/Messages.ts"));
    }

    private static async Task<LanguageAnalysisResult> AnalyzeAsync()
    {
        using var repository = TemporaryRepository.CopyFixture(Fixture, Fixture);
        return await AnalyzeAsync(repository).ConfigureAwait(true);
    }

    private static async Task<LanguageAnalysisResult> AnalyzeAsync(TemporaryRepository repository)
    {
        var analyzer = new TypeScriptLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact[] TextsOf(LanguageAnalysisResult result) =>
        result.Symbols.Where(symbol => symbol.SymbolKind == KnownSymbolKinds.TextLiteral).ToArray();

    private static SymbolFact HolderOf(LanguageAnalysisResult result, string text)
    {
        var literal = Assert.Single(TextsOf(result), candidate => candidate.Name == text);
        var holderId = Assert.Single(
            result.Relationships,
            relationship => relationship.Kind == KnownRelationshipKinds.Contains && relationship.TargetId == literal.Id).SourceId;
        return Assert.Single(result.Symbols, symbol => symbol.Id == holderId);
    }
}

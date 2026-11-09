using Scout.Analysis.Css;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class CssLanguageAnalyzerTests
{
    [Fact]
    public async Task AnalyzeObservesClassesAndCustomProperties()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Succeeded, result.Analyzer.Status);
        Assert.Empty(result.Diagnostics);

        var row = FindSymbol(result, "invoice-row");
        Assert.Equal(KnownSymbolKinds.Type, row.SymbolKind);
        Assert.Equal("css-class", row.NativeKind);
        Assert.Equal(KnownVisibilities.Public, row.Visibility);

        var border = FindSymbol(result, "invoice-border");
        Assert.Equal(KnownSymbolKinds.Value, border.SymbolKind);
        Assert.Equal("css-custom-property", border.NativeKind);

        AssertRelationship(result, KnownRelationshipKinds.References, "invoice-row", "invoice-border");
    }

    [Fact]
    public async Task AnalyzeUsesTheSharedIdentifierOtherAnalyzersDerive()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var row = FindSymbol(result, "invoice-row");
        Assert.Equal(SharedSymbolKey.CreateId(CssLanguageAnalyzer.ClassVocabulary, "invoice-row"), row.Id);

        var padding = FindSymbol(result, "invoice-padding");
        Assert.Equal(
            SharedSymbolKey.CreateId(CssLanguageAnalyzer.CustomPropertyVocabulary, "invoice-padding"),
            padding.Id);
    }

    [Fact]
    public async Task AnalyzeMergesRepeatedDeclarationsIntoOneSymbol()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        // The media query declares '.invoice-cell' a second time; both declarations describe one class.
        var cell = FindSymbol(result, "invoice-cell");
        Assert.Equal(2, cell.DeclarationLocations.Count);
    }

    [Fact]
    public async Task AnalyzeIgnoresClassLikeTextThatIsNotASelector()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        // '.not-a-class' only appears inside a content string, and 'data-state' inside an attribute selector.
        Assert.DoesNotContain(result.Symbols, symbol => symbol.Name == "not-a-class");
        Assert.DoesNotContain(result.Symbols, symbol => symbol.Name == "empty");
        Assert.Contains(result.Symbols, symbol => symbol.Name == "invoice-empty");
    }

    private static async Task<LanguageAnalysisResult> AnalyzeFixtureAsync()
    {
        using var repository = TemporaryRepository.CopyFixture(
            "MinimalTypeScriptRepository",
            "MinimalTypeScriptRepository");

        var analyzer = new CssLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact FindSymbol(LanguageAnalysisResult result, string name)
    {
        return Assert.Single(result.Symbols, symbol => symbol.Name == name);
    }

    private static void AssertRelationship(
        LanguageAnalysisResult result,
        string kind,
        string sourceName,
        string targetName)
    {
        var names = result.Symbols.ToDictionary(symbol => symbol.Id, symbol => symbol.Name);
        var observed = result.Relationships.Any(relationship =>
            relationship.Kind == kind &&
            names.TryGetValue(relationship.SourceId, out var source) &&
            names.TryGetValue(relationship.TargetId, out var target) &&
            source == sourceName &&
            target == targetName);
        Assert.True(observed, $"Expected a '{kind}' relationship from '{sourceName}' to '{targetName}'.");
    }
}

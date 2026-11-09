using Scout.Analysis.TypeScript;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

// JavaScript is analyzed by the TypeScript compiler, but it is not TypeScript. These tests exist so a
// repository without a single type annotation keeps producing the same observed structure, and so the
// facts it produces never claim a language the repository does not use.
public sealed class JavaScriptLanguageAnalyzerTests
{
    [Fact]
    public async Task AnalyzeObservesStructureInUntypedSources()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Succeeded, result.Analyzer.Status);
        Assert.Empty(result.Diagnostics);

        var store = FindSymbol(result, "InMemoryInvoiceStore");
        Assert.Equal(KnownSymbolKinds.Type, store.SymbolKind);
        Assert.Equal(KnownVisibilities.Public, store.Visibility);

        Assert.Equal(KnownVisibilities.Private, FindSymbol(result, "zeroed").Visibility);

        AssertRelationship(result, KnownRelationshipKinds.Calls, "emptyInvoice", "zeroed");
        AssertRelationship(result, KnownRelationshipKinds.Calls, "InvoiceRow", "formatTotal");
    }

    [Fact]
    public async Task AnalyzeObservesReactComponentsWrittenInJsx()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal("react-component", FindSymbol(result, "InvoiceRow").NativeKind);
        AssertRelationship(result, KnownRelationshipKinds.Renders, "InvoiceTable", "InvoiceRow");
    }

    [Fact]
    public async Task AnalyzeReportsJavaScriptRatherThanTypeScript()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var project = Assert.Single(result.Projects);
        Assert.Equal("package.json", project.RelativePath);
        Assert.Equal("javascript", project.LanguageId);

        Assert.Equal(
            "javascript",
            Assert.Single(result.SourceFiles, file => file.RelativePath.EndsWith(".js", StringComparison.Ordinal))
                .LanguageId);
        Assert.All(
            result.SourceFiles.Where(file => file.RelativePath.EndsWith(".jsx", StringComparison.Ordinal)),
            file => Assert.Equal("javascriptreact", file.LanguageId));
    }

    private static async Task<LanguageAnalysisResult> AnalyzeFixtureAsync()
    {
        using var repository = TemporaryRepository.CopyFixture(
            "MinimalJavaScriptRepository",
            "MinimalJavaScriptRepository");

        var analyzer = new TypeScriptLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact FindSymbol(LanguageAnalysisResult result, string declaredName) =>
        Assert.Single(
            result.Symbols,
            symbol => string.Equals(DisplayNameOf(symbol), declaredName, StringComparison.Ordinal));

    private static void AssertRelationship(
        LanguageAnalysisResult result,
        string kind,
        string sourceName,
        string targetName)
    {
        var names = result.Symbols.ToDictionary(symbol => symbol.Id, DisplayNameOf);
        var observed = result.Relationships.Any(relationship =>
            string.Equals(relationship.Kind, kind, StringComparison.Ordinal) &&
            names.TryGetValue(relationship.SourceId, out var source) &&
            names.TryGetValue(relationship.TargetId, out var target) &&
            string.Equals(source, sourceName, StringComparison.Ordinal) &&
            string.Equals(target, targetName, StringComparison.Ordinal));
        Assert.True(observed, $"Expected a '{kind}' relationship from '{sourceName}' to '{targetName}'.");
    }

    private static string DisplayNameOf(SymbolFact symbol)
    {
        if (symbol.QualifiedName is null)
        {
            return symbol.Name;
        }

        var separatorIndex = symbol.QualifiedName.LastIndexOf("::", StringComparison.Ordinal);
        return separatorIndex < 0
            ? symbol.QualifiedName
            : symbol.QualifiedName[(separatorIndex + 2)..];
    }
}

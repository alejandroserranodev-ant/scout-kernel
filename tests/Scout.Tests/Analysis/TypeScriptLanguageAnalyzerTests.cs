using Scout.Analysis.TypeScript;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class TypeScriptLanguageAnalyzerTests
{
    private static readonly string[] ExpectedLanguageIds = ["typescript", "typescriptreact"];

    [Fact]
    public async Task AnalyzeObservesExportedSurfaceAndCrossFileRelationships()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Succeeded, result.Analyzer.Status);
        Assert.Empty(result.Diagnostics);

        var store = FindSymbol(result, "InvoiceStore");
        Assert.Equal(KnownSymbolKinds.Type, store.SymbolKind);
        Assert.Equal(KnownVisibilities.Public, store.Visibility);
        Assert.Equal("interface", store.NativeKind);

        var helper = FindSymbol(result, "zeroed");
        Assert.Equal(KnownVisibilities.Private, helper.Visibility);

        AssertRelationship(result, KnownRelationshipKinds.Implements, "InMemoryInvoiceStore", "InvoiceStore");
        AssertRelationship(result, KnownRelationshipKinds.Inherits, "CancelInvoiceHandler", "InvoiceHandlerBase");
        AssertRelationship(result, KnownRelationshipKinds.Calls, "CancelInvoiceHandler.handle", "zeroed");
    }

    [Fact]
    public async Task AnalyzeObservesReactComponentsAndJsxUsage()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var row = FindSymbol(result, "InvoiceRow");
        Assert.Equal("react-component", row.NativeKind);
        Assert.Equal(KnownVisibilities.Public, row.Visibility);

        AssertRelationship(result, KnownRelationshipKinds.Renders, "InvoiceTable", "InvoiceRow");
        AssertRelationship(result, KnownRelationshipKinds.Renders, "InvoiceSummary", "InvoiceRow");
    }

    [Fact]
    public async Task AnalyzeObservesTheManifestBackedProject()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var project = Assert.Single(result.Projects);
        Assert.Equal("tsconfig.json", project.RelativePath);
        Assert.NotEmpty(result.SourceFiles);
        Assert.All(
            result.SourceFiles,
            file => Assert.Contains(file.LanguageId, ExpectedLanguageIds, StringComparer.Ordinal));
        Assert.Contains(result.SourceFiles, file => file.LanguageId == "typescriptreact");
    }

    private static async Task<LanguageAnalysisResult> AnalyzeFixtureAsync()
    {
        using var repository = TemporaryRepository.CopyFixture(
            "MinimalTypeScriptRepository",
            "MinimalTypeScriptRepository");

        var analyzer = new TypeScriptLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact FindSymbol(LanguageAnalysisResult result, string declaredName)
    {
        var matches = result.Symbols
            .Where(symbol => string.Equals(DisplayNameOf(symbol), declaredName, StringComparison.Ordinal))
            .ToArray();
        Assert.True(
            matches.Length == 1,
            $"Expected exactly one symbol named '{declaredName}' but found {matches.Length}.");
        return matches[0];
    }

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

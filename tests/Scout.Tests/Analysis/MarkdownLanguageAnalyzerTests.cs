using Scout.Analysis.Markdown;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class MarkdownLanguageAnalyzerTests
{
    [Fact]
    public async Task AnalyzeObservesEverySectionOfTheOutline()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal(KnownAnalyzerStatuses.Succeeded, result.Analyzer.Status);
        Assert.Empty(result.Diagnostics);

        var readme = result.Symbols
            .Where(symbol => symbol.QualifiedName!.StartsWith("README.md#", StringComparison.Ordinal))
            .Select(symbol => symbol.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            ["Billing Service", "Configure", "Deployment", "Deployment", "Getting started", "Install", "Install"],
            readme);

        var section = FindSymbol(result, "README.md#Billing Service > Getting started");
        Assert.Equal(KnownSymbolKinds.DocumentSection, section.SymbolKind);
        Assert.Equal(MarkdownLanguageAnalyzer.SectionNativeKind, section.NativeKind);
        Assert.Equal(KnownVisibilities.Unknown, section.Visibility);
        Assert.Equal("markdown", section.LanguageId);
    }

    [Fact]
    public async Task AnalyzeNestsSectionsUnderTheirEnclosingHeading()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        AssertRelationship(
            result,
            KnownRelationshipKinds.Contains,
            "README.md#Billing Service",
            "README.md#Billing Service > Getting started");
        AssertRelationship(
            result,
            KnownRelationshipKinds.Contains,
            "README.md#Billing Service > Getting started",
            "README.md#Billing Service > Getting started > Configure");

        // A heading that skips a level still nests under the nearest enclosing heading.
        AssertRelationship(
            result,
            KnownRelationshipKinds.Contains,
            "docs/guide.md#Invoice guide and code",
            "docs/guide.md#Invoice guide and code > Skipped a level");

        var fileId = StructuralId.Create(KnownEntityKinds.SourceFile, "README.md");
        var topLevel = FindSymbol(result, "README.md#Billing Service");
        Assert.Contains(
            result.Relationships,
            relationship => relationship.Kind == KnownRelationshipKinds.Defines &&
                relationship.SourceId == fileId &&
                relationship.TargetId == topLevel.Id);
    }

    [Fact]
    public async Task AnalyzeKeepsRepeatedOutlinePathsAsDistinctSections()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        // "Install" appears under two parents and "Deployment" twice under the same one.
        Assert.Equal(2, result.Symbols.Count(symbol => symbol.Name == "Install"));
        FindSymbol(result, "README.md#Billing Service > Deployment");
        FindSymbol(result, "README.md#Billing Service > Deployment (2)");
        Assert.Equal(result.Symbols.Count, result.Symbols.Select(symbol => symbol.Id).Distinct().Count());
    }

    [Fact]
    public async Task AnalyzeIgnoresTextThatOnlyLooksLikeAHeading()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var guide = result.Symbols
            .Where(symbol => symbol.QualifiedName!.StartsWith("docs/guide.md#", StringComparison.Ordinal))
            .Select(symbol => symbol.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Front matter, a fenced block, an HTML comment, indented code, '#hashtag' and seven markers are not headings.
        Assert.Equal(["Invoice guide and code", "Notes on C#", "Skipped a level"], guide);
    }

    [Fact]
    public async Task AnalyzeRecordsTheHeadingLine()
    {
        var result = await AnalyzeFixtureAsync().ConfigureAwait(true);

        var section = FindSymbol(result, "docs/guide.md#Invoice guide and code");
        var location = Assert.Single(section.DeclarationLocations);
        Assert.Equal("docs/guide.md", location.RelativePath);
        Assert.Equal(6, location.StartLine);
        Assert.Equal(1, location.StartColumn);
    }

    [Fact]
    public async Task AnalyzeReportsADocumentWithoutHeadingsAsASourceFileOnly()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalMarkdownRepository", "MinimalMarkdownRepository");
        repository.WriteFile("NOTES.md", "Just prose, no outline.\n");

        var result = await AnalyzeAsync(repository).ConfigureAwait(true);

        Assert.Contains(result.SourceFiles, file => file.RelativePath == "NOTES.md");
        Assert.DoesNotContain(result.Symbols, symbol => symbol.QualifiedName!.StartsWith("NOTES.md#", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnalyzeProducesTheSameFactsForTheSameRepository()
    {
        var first = await AnalyzeFixtureAsync().ConfigureAwait(true);
        var second = await AnalyzeFixtureAsync().ConfigureAwait(true);

        Assert.Equal(first.Symbols.Select(symbol => symbol.Id), second.Symbols.Select(symbol => symbol.Id));
        Assert.Equal(first.Relationships.Select(relationship => relationship.Id), second.Relationships.Select(relationship => relationship.Id));
    }

    private static async Task<LanguageAnalysisResult> AnalyzeFixtureAsync()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalMarkdownRepository", "MinimalMarkdownRepository");
        return await AnalyzeAsync(repository).ConfigureAwait(true);
    }

    private static async Task<LanguageAnalysisResult> AnalyzeAsync(TemporaryRepository repository)
    {
        var analyzer = new MarkdownLanguageAnalyzer();
        var descriptor = await new RepositoryDiscovery(analyzer.SupportedFileExtensions)
            .DiscoverAsync(repository.RootPath)
            .ConfigureAwait(true);
        return await analyzer.AnalyzeAsync(descriptor).ConfigureAwait(true);
    }

    private static SymbolFact FindSymbol(LanguageAnalysisResult result, string qualifiedName)
    {
        return Assert.Single(result.Symbols, symbol => symbol.QualifiedName == qualifiedName);
    }

    private static void AssertRelationship(
        LanguageAnalysisResult result,
        string kind,
        string sourceQualifiedName,
        string targetQualifiedName)
    {
        var names = result.Symbols.ToDictionary(symbol => symbol.Id, symbol => symbol.QualifiedName);
        var observed = result.Relationships.Any(relationship =>
            relationship.Kind == kind &&
            names.TryGetValue(relationship.SourceId, out var source) &&
            names.TryGetValue(relationship.TargetId, out var target) &&
            source == sourceQualifiedName &&
            target == targetQualifiedName);
        Assert.True(observed, $"Expected a '{kind}' relationship from '{sourceQualifiedName}' to '{targetQualifiedName}'.");
    }
}

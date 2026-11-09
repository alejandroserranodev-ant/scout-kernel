using Scout.Analysis.Css;
using Scout.Analysis.TypeScript;
using Scout.Core.Analysis;
using Scout.Core.Compilation;
using Scout.Core.Ir;
using Scout.Tests.Support;

namespace Scout.Tests.Analysis;

public sealed class CrossAnalyzerReferenceTests
{
    [Fact]
    public async Task CompileResolvesStyleClassReferencesAcrossAnalyzers()
    {
        using var repository = TemporaryRepository.CopyFixture(
            "MinimalTypeScriptRepository",
            "MinimalTypeScriptRepository");
        var outputPath = Path.Combine(repository.WorkspacePath, "kernel");

        var result = await new ScoutCompiler([new TypeScriptLanguageAnalyzer(), new CssLanguageAnalyzer()])
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, outputPath))
            .ConfigureAwait(true);

        var classId = SharedSymbolKey.CreateId(CssLanguageAnalyzer.ClassVocabulary, "invoice-row");
        var declaring = result.Graph.GetNode(classId);
        Assert.Equal("invoice-row", declaring.Name);

        var component = result.Structural.Symbols.Single(symbol => symbol.Name == "InvoiceRow");
        Assert.Contains(
            result.Structural.Relationships,
            relationship =>
                relationship.Kind == KnownRelationshipKinds.References &&
                relationship.SourceId == component.Id &&
                relationship.TargetId == classId);
    }

    [Fact]
    public async Task CompileDropsUnresolvedReferencesWithAnExplicitDiagnostic()
    {
        using var repository = TemporaryRepository.CopyFixture(
            "MinimalTypeScriptRepository",
            "MinimalTypeScriptRepository");
        var outputPath = Path.Combine(repository.WorkspacePath, "kernel");

        // Without the CSS analyzer nothing declares the style classes the components name.
        var result = await new ScoutCompiler([new TypeScriptLanguageAnalyzer()])
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, outputPath))
            .ConfigureAwait(true);

        var classId = SharedSymbolKey.CreateId(CssLanguageAnalyzer.ClassVocabulary, "invoice-row");
        Assert.False(result.Graph.TryGetNode(classId, out _));
        Assert.DoesNotContain(result.Structural.Relationships, relationship => relationship.TargetId == classId);
        Assert.Contains(
            result.Structural.Diagnostics,
            diagnostic =>
                diagnostic.Code == "UNRESOLVED_RELATIONSHIP_ENDPOINT" &&
                diagnostic.Severity == KnownDiagnosticSeverities.Warning);
    }
}

using Scout.Core.Graph;
using Scout.Core.Intelligence;

namespace Scout.Tests.Intelligence;

public sealed class BasicIntelligenceCompilerTests
{
    [Fact]
    public void CompileEmitsEvidenceBackedScopedConventionAtThreshold()
    {
        var structural = HandcraftedSnapshotFactory.CreateConventionSnapshot(
            "CreateInvoiceHandler",
            "UpdateInvoiceHandler",
            "CancelInvoiceHandler",
            "LegacyInvoiceProcessor");
        var graph = new ProjectGraph(structural);

        var intelligence = BasicIntelligenceCompiler.Compile(
            graph,
            HandcraftedSnapshotFactory.CreatedAtUtc);

        var convention = Assert.Single(intelligence.Conventions);
        Assert.Equal("*Handler", convention.Pattern);
        Assert.Equal(3, convention.Support);
        Assert.Equal(4, convention.Population);
        Assert.Equal(KnowledgeClassification.Inference, convention.Metadata.Classification);
        Assert.Equal(0.75, convention.Metadata.Confidence.Value);
        Assert.Equal($"{HandcraftedSnapshotFactory.HandlerDirectory}/**", convention.Metadata.Scope);
        Assert.Equal(KnowledgeFreshness.Unknown, convention.Metadata.Freshness);

        var handlers = structural.Symbols
            .Where(symbol => symbol.Name.EndsWith("Handler", StringComparison.Ordinal))
            .ToArray();
        var expectedEvidenceIds = handlers
            .SelectMany(symbol => symbol.EvidenceIds)
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal(expectedEvidenceIds, convention.Metadata.EvidenceIds);
        Assert.Equal(
            Assert.Single(structural.Symbols, symbol => symbol.Name == "LegacyInvoiceProcessor").Id,
            Assert.Single(convention.Metadata.ExceptionEntityIds));
    }

    [Fact]
    public void CompileDoesNotEmitConventionBelowAdherenceThreshold()
    {
        var structural = HandcraftedSnapshotFactory.CreateConventionSnapshot(
            "CreateInvoiceHandler",
            "UpdateInvoiceHandler",
            "CancelInvoiceHandler",
            "LegacyInvoiceProcessor",
            "ScheduledInvoiceProcessor");
        var graph = new ProjectGraph(structural);

        var intelligence = BasicIntelligenceCompiler.Compile(
            graph,
            HandcraftedSnapshotFactory.CreatedAtUtc);

        Assert.Empty(intelligence.Conventions);
    }
}

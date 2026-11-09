using Scout.Core.Graph;
using Scout.Core.Intelligence;
using Scout.Core.Ir;

namespace Scout.Tests.Intelligence;

public sealed class ContractCompilerTests
{
    [Fact]
    public void CompileTreatsConsumedAbstractionAsContractWithItsBlastRadius()
    {
        var structural = HandcraftedSnapshotFactory.CreateContractSnapshot();
        var graph = new ProjectGraph(structural);

        var intelligence = BasicIntelligenceCompiler.Compile(
            graph,
            HandcraftedSnapshotFactory.CreatedAtUtc);

        var contract = Assert.Single(intelligence.Contracts);
        Assert.Equal("IInvoiceStore", contract.Name);
        Assert.Equal(KnownContractKinds.Abstraction, contract.ContractKind);
        Assert.Equal(KnownVisibilities.Public, contract.Visibility);
        Assert.Equal("src/Billing/Application/IInvoiceStore.cs", contract.RelativePath);
        Assert.Equal("src/Billing/Application/**", contract.Metadata.Scope);
        Assert.Equal(KnowledgeClassification.Inference, contract.Metadata.Classification);
        Assert.Equal(0.9, contract.Metadata.Confidence.Value);
        Assert.NotNull(contract.DeclaringModuleId);

        Assert.Equal(5, contract.BlastRadius);
        var implementor = Assert.Single(
            contract.Consumers,
            consumer => consumer.Name == "InMemoryInvoiceStore");
        Assert.Contains(KnownRelationshipKinds.Implements, implementor.RelationshipKinds);
        Assert.Equal(contract.DeclaringModuleId, implementor.ModuleId);
        Assert.Equal(4, contract.Consumers.Count(consumer => consumer.Name.EndsWith("Handler", StringComparison.Ordinal)));
        Assert.Equal(Assert.Single(contract.ConsumerModuleIds), contract.DeclaringModuleId);
        Assert.DoesNotContain("contracts", intelligence.UnknownAreas);
    }

    [Fact]
    public void CompileReportsContractsAsUnknownWhenNoSymbolIsConsumed()
    {
        var structural = HandcraftedSnapshotFactory.CreateKernelSnapshot();
        var graph = new ProjectGraph(structural);

        var intelligence = BasicIntelligenceCompiler.Compile(
            graph,
            HandcraftedSnapshotFactory.CreatedAtUtc);

        Assert.Empty(intelligence.Contracts);
        Assert.Contains("contracts", intelligence.UnknownAreas);
    }

    [Fact]
    public void CompileExcludesSelfConsumptionFromBlastRadius()
    {
        var structural = HandcraftedSnapshotFactory.CreateContractSnapshot();
        var graph = new ProjectGraph(structural);

        var intelligence = BasicIntelligenceCompiler.Compile(
            graph,
            HandcraftedSnapshotFactory.CreatedAtUtc);

        var contract = Assert.Single(intelligence.Contracts);
        Assert.DoesNotContain(contract.Consumers, consumer => consumer.SymbolId == contract.SymbolId);
    }
}

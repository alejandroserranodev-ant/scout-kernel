using System.Globalization;
using Scout.Core.Graph;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Derives contracts from observed consumption rather than from declaration syntax alone.
/// </summary>
/// <remarks>
/// <para>
/// A declared public symbol is not a contract. A symbol becomes a contract when other symbols already depend on
/// its shape, because that dependency is what makes a change expensive. The compiler therefore starts from
/// incoming consumption edges and uses visibility only to qualify and score the resulting claim.
/// </para>
/// <para>
/// Consumers are rolled up to their outermost declaring type so that blast radius counts the types a reviewer must
/// check, and self-consumption from a contract's own members is excluded.
/// </para>
/// </remarks>
internal static class ContractCompiler
{
    private const int MinimumSharedConsumers = 2;
    private const int MinimumEnforcedConsumers = 1;
    private const double EnforcedBaseConfidence = 0.9;
    private const double ObservedBaseConfidence = 0.7;
    private const double CrossModuleConfidenceBonus = 0.05;
    private const double MaximumConfidence = 0.95;

    private static readonly string[] ConsumptionRelationshipKinds =
    [
        KnownRelationshipKinds.Calls,
        KnownRelationshipKinds.Implements,
        KnownRelationshipKinds.Inherits,
        KnownRelationshipKinds.References,
        KnownRelationshipKinds.Renders
    ];

    private static readonly string[] CompilerEnforcedRelationshipKinds =
    [
        KnownRelationshipKinds.Implements,
        KnownRelationshipKinds.Inherits
    ];

    internal static ContractIntelligence[] Compile(ProjectGraph graph, IReadOnlyList<ModuleIntelligence> modules)
    {
        var ownership = ModuleOwnershipIndex.Create(graph, modules);
        var consumptionsByTarget = GroupConsumptionsByBoundary(graph);
        var contracts = new List<ContractIntelligence>();

        foreach (var (targetId, consumptions) in consumptionsByTarget.OrderBy(entry => entry.Key))
        {
            if (graph.GetNode(targetId) is not SymbolFact symbol)
            {
                continue;
            }

            var contract = TryCompileContract(graph, ownership, symbol, consumptions);
            if (contract is not null)
            {
                contracts.Add(contract);
            }
        }

        return contracts.OrderBy(contract => contract.Id).ToArray();
    }

    /// <summary>
    /// Attributes every consumption edge to the outermost declaring type on both sides.
    /// </summary>
    /// <remarks>
    /// Calling a member is a dependency on the shape of the type that declares it, so member consumption is
    /// attributed to that type instead of producing a second, redundant contract per member. Symbols that are not
    /// declared inside a type, such as free functions and React components, remain their own boundary.
    /// </remarks>
    private static Dictionary<StructuralId, List<Consumption>> GroupConsumptionsByBoundary(ProjectGraph graph)
    {
        var consumptionsByTarget = new Dictionary<StructuralId, List<Consumption>>();

        foreach (var symbol in graph.Snapshot.Symbols)
        {
            if (symbol.SymbolKind == KnownSymbolKinds.Namespace)
            {
                continue;
            }

            var targetId = ResolveOutermostType(graph, symbol.Id);
            foreach (var edge in graph.GetIncomingEdges(symbol.Id))
            {
                if (!ConsumptionRelationshipKinds.Contains(edge.Kind, StringComparer.Ordinal))
                {
                    continue;
                }

                var consumerId = ResolveOutermostType(graph, edge.SourceId);
                if (consumerId == targetId || graph.GetNode(consumerId) is not SymbolFact)
                {
                    continue;
                }

                if (!consumptionsByTarget.TryGetValue(targetId, out var consumptions))
                {
                    consumptions = [];
                    consumptionsByTarget[targetId] = consumptions;
                }

                consumptions.Add(new Consumption(edge, consumerId));
            }
        }

        return consumptionsByTarget;
    }

    private static ContractIntelligence? TryCompileContract(
        ProjectGraph graph,
        ModuleOwnershipIndex ownership,
        SymbolFact symbol,
        IReadOnlyList<Consumption> consumptions)
    {
        if (symbol.DeclarationLocations.Count == 0)
        {
            return null;
        }

        var consumerGroups = consumptions
            .GroupBy(consumption => consumption.ConsumerId)
            .OrderBy(group => group.Key)
            .ToArray();
        var isCompilerEnforced = consumptions.Any(consumption =>
            CompilerEnforcedRelationshipKinds.Contains(consumption.Edge.Kind, StringComparer.Ordinal));
        if (!QualifiesAsContract(symbol, consumerGroups.Length, isCompilerEnforced))
        {
            return null;
        }

        var consumers = consumerGroups
            .Select(group => new ContractConsumer(
                group.Key,
                ((SymbolFact)graph.GetNode(group.Key)).Name,
                ownership.Find(group.Key),
                group.Select(consumption => consumption.Edge.Kind)))
            .ToArray();
        var consumerModuleCount = consumers
            .Select(consumer => consumer.ModuleId)
            .Where(moduleId => moduleId.HasValue)
            .Distinct()
            .Count();
        var contractKind = ResolveContractKind(symbol, consumptions);
        var relativePath = symbol.DeclarationLocations[0].RelativePath;
        var evidenceIds = symbol.EvidenceIds
            .Concat(consumptions.SelectMany(consumption => consumption.Edge.EvidenceIds));

        return new ContractIntelligence(
            StructuralId.Create("contract", symbol.Id.Value),
            symbol.Id,
            symbol.Name,
            BuildSummary(contractKind, consumers.Length, consumerModuleCount, isCompilerEnforced),
            contractKind,
            symbol.SymbolKind,
            symbol.Visibility,
            symbol.QualifiedName,
            symbol.Signature,
            ownership.Find(symbol.Id),
            relativePath,
            consumers,
            new KnowledgeMetadata(
                KnowledgeClassification.Inference,
                new ConfidenceScore(ResolveConfidence(isCompilerEnforced, consumerModuleCount)),
                RepositoryScope.FromRelativePath(relativePath),
                KnowledgeFreshness.Unknown,
                evidenceIds));
    }

    private static bool QualifiesAsContract(SymbolFact symbol, int consumerCount, bool isCompilerEnforced)
    {
        var isExternallyVisible = symbol.Visibility is KnownVisibilities.Public or KnownVisibilities.Protected;
        if (!isExternallyVisible && !isCompilerEnforced)
        {
            return false;
        }

        return isCompilerEnforced
            ? consumerCount >= MinimumEnforcedConsumers
            : consumerCount >= MinimumSharedConsumers;
    }

    private static string ResolveContractKind(SymbolFact symbol, IEnumerable<Consumption> consumptions)
    {
        var kinds = consumptions
            .Select(consumption => consumption.Edge.Kind)
            .ToHashSet(StringComparer.Ordinal);

        // The kind follows observed consumption. Declaration syntax alone, such as being an interface, says
        // nothing about how a symbol is actually depended upon.
        if (kinds.Contains(KnownRelationshipKinds.Implements))
        {
            return KnownContractKinds.Abstraction;
        }

        if (kinds.Contains(KnownRelationshipKinds.Inherits))
        {
            return KnownContractKinds.InheritanceRoot;
        }

        if (kinds.Contains(KnownRelationshipKinds.Renders) ||
            string.Equals(symbol.NativeKind, "react-component", StringComparison.Ordinal))
        {
            return KnownContractKinds.ComponentSurface;
        }

        return symbol.SymbolKind switch
        {
            KnownSymbolKinds.Callable => KnownContractKinds.CallableSurface,
            KnownSymbolKinds.Value => KnownContractKinds.SharedValue,
            _ => KnownContractKinds.SharedType
        };
    }

    private static double ResolveConfidence(bool isCompilerEnforced, int consumerModuleCount)
    {
        var baseConfidence = isCompilerEnforced ? EnforcedBaseConfidence : ObservedBaseConfidence;
        var crossModuleBonus = CrossModuleConfidenceBonus * Math.Max(consumerModuleCount - 1, 0);
        return Math.Min(MaximumConfidence, baseConfidence + crossModuleBonus);
    }

    private static string BuildSummary(
        string contractKind,
        int consumerCount,
        int consumerModuleCount,
        bool isCompilerEnforced)
    {
        var moduleScope = consumerModuleCount switch
        {
            0 => "modules that could not be resolved",
            1 => "1 module",
            _ => string.Create(CultureInfo.InvariantCulture, $"{consumerModuleCount} modules")
        };
        var basis = isCompilerEnforced ? "compiler-enforced" : "observed usage";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{contractKind} with {consumerCount} consumer(s) across {moduleScope}, established by {basis}. Changing its shape affects every listed consumer.");
    }

    private static StructuralId ResolveOutermostType(ProjectGraph graph, StructuralId symbolId)
    {
        var current = symbolId;
        while (true)
        {
            var declaringType = graph
                .GetIncomingEdges(current, KnownRelationshipKinds.Defines)
                .FirstOrDefault(edge => graph.GetNode(edge.SourceId) is SymbolFact { SymbolKind: KnownSymbolKinds.Type });
            if (declaringType is null)
            {
                return current;
            }

            current = declaringType.SourceId;
        }
    }

    /// <summary>
    /// Pairs a consumption edge with the boundary symbol that owns the consumer.
    /// </summary>
    private readonly record struct Consumption(GraphEdge Edge, StructuralId ConsumerId);
}

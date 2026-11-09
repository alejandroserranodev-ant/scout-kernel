using Scout.Core.Graph;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Resolves which inferred module owns a declared symbol by walking project containment and declaration edges.
/// </summary>
/// <remarks>
/// Ownership is resolved once and reused by every intelligence compiler that needs to reason about module
/// boundaries. When a symbol is reachable from more than one module, the lowest-ordered module wins so that
/// ownership stays deterministic across compilations.
/// </remarks>
internal sealed class ModuleOwnershipIndex
{
    private readonly IReadOnlyDictionary<StructuralId, StructuralId> moduleIdBySymbolId;

    private ModuleOwnershipIndex(IReadOnlyDictionary<StructuralId, StructuralId> moduleIdBySymbolId)
    {
        this.moduleIdBySymbolId = moduleIdBySymbolId;
    }

    internal static ModuleOwnershipIndex Create(ProjectGraph graph, IEnumerable<ModuleIntelligence> modules)
    {
        var moduleIdBySymbolId = new Dictionary<StructuralId, StructuralId>();
        foreach (var module in modules.OrderBy(module => module.Id))
        {
            if (!graph.TryGetNode(module.SourceProjectId, out _))
            {
                continue;
            }

            var containedFileIds = graph
                .GetOutgoingEdges(module.SourceProjectId, KnownRelationshipKinds.Contains)
                .Where(edge => graph.GetNode(edge.TargetId) is SourceFileFact)
                .Select(edge => edge.TargetId);
            foreach (var fileId in containedFileIds)
            {
                AssignDefinedSymbols(graph, fileId, module.Id, moduleIdBySymbolId);
            }
        }

        return new ModuleOwnershipIndex(moduleIdBySymbolId);
    }

    internal StructuralId? Find(StructuralId symbolId) =>
        moduleIdBySymbolId.TryGetValue(symbolId, out var moduleId) ? moduleId : null;

    private static void AssignDefinedSymbols(
        ProjectGraph graph,
        StructuralId declaringEntityId,
        StructuralId moduleId,
        Dictionary<StructuralId, StructuralId> moduleIdBySymbolId)
    {
        foreach (var edge in graph.GetOutgoingEdges(declaringEntityId, KnownRelationshipKinds.Defines))
        {
            if (graph.GetNode(edge.TargetId) is not SymbolFact)
            {
                continue;
            }

            if (!moduleIdBySymbolId.TryAdd(edge.TargetId, moduleId))
            {
                continue;
            }

            AssignDefinedSymbols(graph, edge.TargetId, moduleId, moduleIdBySymbolId);
        }
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using Scout.Core.Ir;

namespace Scout.Core.Graph;

/// <summary>
/// Provides a validated and indexed graph view derived entirely from a structural snapshot.
/// </summary>
/// <remarks>
/// The graph is not a separate persistence model. Recreate it from <see cref="StructuralSnapshot"/> whenever
/// indexed traversal is required.
/// </remarks>
public sealed class ProjectGraph
{
    private readonly ReadOnlyDictionary<StructuralId, StructuralEntity> _nodesById;
    private readonly IReadOnlyDictionary<StructuralId, IReadOnlyList<GraphEdge>> _outgoingByNodeId;
    private readonly IReadOnlyDictionary<StructuralId, IReadOnlyList<GraphEdge>> _incomingByNodeId;

    /// <summary>
    /// Initializes a validated graph view from a structural snapshot.
    /// </summary>
    /// <param name="snapshot">The structural snapshot that remains the source of truth.</param>
    /// <exception cref="ProjectGraphValidationException">
    /// Thrown when entity identifiers are duplicated, relationship identifiers conflict, or an endpoint is absent.
    /// </exception>
    public ProjectGraph(StructuralSnapshot snapshot)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

        var nodes = snapshot.Projects
            .Cast<StructuralEntity>()
            .Concat(snapshot.SourceFiles)
            .Concat(snapshot.Symbols)
            .OrderBy(static node => node.Id)
            .ToArray();

        var mutableNodesById = new Dictionary<StructuralId, StructuralEntity>();
        foreach (var node in nodes)
        {
            if (!mutableNodesById.TryAdd(node.Id, node))
            {
                throw new ProjectGraphValidationException(
                    $"Structural entity identifier '{node.Id}' is used by more than one entity.");
            }
        }

        var groupedRelationships = ValidateAndGroupRelationships(snapshot.Relationships, mutableNodesById);
        var edges = groupedRelationships
            .Select(static group => CreateEdge(group.Key, group.Value))
            .OrderBy(static edge => edge.SourceId)
            .ThenBy(static edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetId)
            .ToArray();

        Nodes = new ReadOnlyCollection<StructuralEntity>(nodes);
        Edges = new ReadOnlyCollection<GraphEdge>(edges);
        _nodesById = new ReadOnlyDictionary<StructuralId, StructuralEntity>(mutableNodesById);
        _outgoingByNodeId = BuildEdgeIndex(nodes, edges, static edge => edge.SourceId);
        _incomingByNodeId = BuildEdgeIndex(nodes, edges, static edge => edge.TargetId);
    }

    /// <summary>
    /// Gets the structural snapshot that is the graph's source of truth.
    /// </summary>
    public StructuralSnapshot Snapshot { get; }

    /// <summary>
    /// Gets every graph entity in deterministic identifier order.
    /// </summary>
    public IReadOnlyList<StructuralEntity> Nodes { get; }

    /// <summary>
    /// Gets every deduplicated edge in deterministic source-kind-target order.
    /// </summary>
    public IReadOnlyList<GraphEdge> Edges { get; }

    /// <summary>
    /// Gets a graph entity by its stable identifier.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <returns>The matching structural entity.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the entity is absent.</exception>
    public StructuralEntity GetNode(StructuralId id)
    {
        if (_nodesById.TryGetValue(id, out var node))
        {
            return node;
        }

        throw new KeyNotFoundException($"Structural entity '{id}' does not exist in the graph.");
    }

    /// <summary>
    /// Attempts to get a graph entity by its stable identifier.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="node">The matching entity when found.</param>
    /// <returns><see langword="true"/> when the entity exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetNode(StructuralId id, [NotNullWhen(true)] out StructuralEntity? node) =>
        _nodesById.TryGetValue(id, out node);

    /// <summary>
    /// Gets outgoing edges for an entity, optionally restricted to one relationship kind.
    /// </summary>
    /// <param name="id">The source entity identifier.</param>
    /// <param name="relationshipKind">The optional extensible relationship kind.</param>
    /// <returns>The matching edges in deterministic order.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the entity is absent.</exception>
    public IReadOnlyList<GraphEdge> GetOutgoingEdges(StructuralId id, string? relationshipKind = null) =>
        GetEdges(_outgoingByNodeId, id, relationshipKind);

    /// <summary>
    /// Gets incoming edges for an entity, optionally restricted to one relationship kind.
    /// </summary>
    /// <param name="id">The target entity identifier.</param>
    /// <param name="relationshipKind">The optional extensible relationship kind.</param>
    /// <returns>The matching edges in deterministic order.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the entity is absent.</exception>
    public IReadOnlyList<GraphEdge> GetIncomingEdges(StructuralId id, string? relationshipKind = null) =>
        GetEdges(_incomingByNodeId, id, relationshipKind);

    private static Dictionary<EdgeKey, List<RelationshipFact>> ValidateAndGroupRelationships(
        IEnumerable<RelationshipFact> relationships,
        Dictionary<StructuralId, StructuralEntity> nodesById)
    {
        var groups = new Dictionary<EdgeKey, List<RelationshipFact>>();
        var identityKeys = new Dictionary<StructuralId, EdgeKey>();

        foreach (var relationship in relationships)
        {
            if (!nodesById.ContainsKey(relationship.SourceId))
            {
                throw new ProjectGraphValidationException(
                    $"Relationship '{relationship.Id}' references missing source entity '{relationship.SourceId}'.");
            }

            if (!nodesById.ContainsKey(relationship.TargetId))
            {
                throw new ProjectGraphValidationException(
                    $"Relationship '{relationship.Id}' references missing target entity '{relationship.TargetId}'.");
            }

            var key = new EdgeKey(relationship.SourceId, relationship.Kind, relationship.TargetId);
            if (identityKeys.TryGetValue(relationship.Id, out var existingKey) && existingKey != key)
            {
                throw new ProjectGraphValidationException(
                    $"Relationship identifier '{relationship.Id}' describes more than one source-kind-target tuple.");
            }

            identityKeys[relationship.Id] = key;
            if (!groups.TryGetValue(key, out var group))
            {
                group = [];
                groups.Add(key, group);
            }

            group.Add(relationship);
        }

        return groups;
    }

    private static GraphEdge CreateEdge(EdgeKey key, IEnumerable<RelationshipFact> relationships)
    {
        var relationshipArray = relationships.ToArray();
        var confidence = new ConfidenceScore(relationshipArray.Max(static item => item.Confidence.Value));
        return new GraphEdge(
            key.Kind,
            key.SourceId,
            key.TargetId,
            confidence,
            relationshipArray.Select(static item => item.Id),
            relationshipArray.SelectMany(static item => item.EvidenceIds));
    }

    private static ReadOnlyDictionary<StructuralId, IReadOnlyList<GraphEdge>> BuildEdgeIndex(
        IEnumerable<StructuralEntity> nodes,
        IEnumerable<GraphEdge> edges,
        Func<GraphEdge, StructuralId> keySelector)
    {
        var mutable = nodes.ToDictionary(
            static node => node.Id,
            static _ => new List<GraphEdge>());

        foreach (var edge in edges)
        {
            mutable[keySelector(edge)].Add(edge);
        }

        var readOnly = mutable.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<GraphEdge>)new ReadOnlyCollection<GraphEdge>(pair.Value));
        return new ReadOnlyDictionary<StructuralId, IReadOnlyList<GraphEdge>>(readOnly);
    }

    private static IReadOnlyList<GraphEdge> GetEdges(
        IReadOnlyDictionary<StructuralId, IReadOnlyList<GraphEdge>> index,
        StructuralId id,
        string? relationshipKind)
    {
        if (!index.TryGetValue(id, out var edges))
        {
            throw new KeyNotFoundException($"Structural entity '{id}' does not exist in the graph.");
        }

        if (relationshipKind is null)
        {
            return edges;
        }

        var normalizedKind = IrGuard.NormalizeKind(relationshipKind, nameof(relationshipKind));
        var matches = edges.Where(edge => StringComparer.Ordinal.Equals(edge.Kind, normalizedKind)).ToArray();
        return new ReadOnlyCollection<GraphEdge>(matches);
    }

    private readonly record struct EdgeKey(StructuralId SourceId, string Kind, StructuralId TargetId);
}

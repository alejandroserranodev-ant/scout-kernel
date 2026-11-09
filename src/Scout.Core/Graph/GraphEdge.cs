using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Graph;

/// <summary>
/// Represents one deduplicated edge derived from one or more structural relationship facts.
/// </summary>
public sealed class GraphEdge
{
    internal GraphEdge(
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        ConfidenceScore confidence,
        IEnumerable<StructuralId> relationshipIds,
        IEnumerable<StructuralId> evidenceIds)
    {
        Kind = kind;
        SourceId = sourceId;
        TargetId = targetId;
        Confidence = confidence;
        Id = StructuralId.Create("graph-edge", sourceId.Value, kind, targetId.Value);
        RelationshipIds = ToOrderedReadOnly(relationshipIds);
        EvidenceIds = ToOrderedReadOnly(evidenceIds);
    }

    /// <summary>
    /// Gets the deterministic identity of the source-kind-target edge tuple.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the extensible relationship kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the source entity identifier.
    /// </summary>
    public StructuralId SourceId { get; }

    /// <summary>
    /// Gets the target entity identifier.
    /// </summary>
    public StructuralId TargetId { get; }

    /// <summary>
    /// Gets the highest confidence among the relationship facts represented by this edge.
    /// </summary>
    public ConfidenceScore Confidence { get; }

    /// <summary>
    /// Gets the ordered relationship fact identifiers represented by this edge.
    /// </summary>
    public IReadOnlyList<StructuralId> RelationshipIds { get; }

    /// <summary>
    /// Gets the ordered union of evidence identifiers from the represented relationship facts.
    /// </summary>
    public IReadOnlyList<StructuralId> EvidenceIds { get; }

    private static ReadOnlyCollection<StructuralId> ToOrderedReadOnly(IEnumerable<StructuralId> values)
    {
        var array = values.Distinct().Order().ToArray();
        return new ReadOnlyCollection<StructuralId>(array);
    }
}

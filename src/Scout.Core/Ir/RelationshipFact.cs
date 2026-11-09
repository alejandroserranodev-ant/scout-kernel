namespace Scout.Core.Ir;

/// <summary>
/// Describes a directed, analyzer-observed relationship between structural entities.
/// </summary>
public sealed class RelationshipFact
{
    /// <summary>
    /// Initializes a structural relationship fact.
    /// </summary>
    /// <param name="id">The stable relationship fact identifier.</param>
    /// <param name="kind">The extensible relationship kind.</param>
    /// <param name="sourceId">The source entity identifier.</param>
    /// <param name="targetId">The target entity identifier.</param>
    /// <param name="confidence">The analyzer confidence in the relationship.</param>
    /// <param name="evidenceIds">The evidence identifiers supporting the relationship.</param>
    public RelationshipFact(
        StructuralId id,
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        ConfidenceScore confidence,
        IEnumerable<StructuralId>? evidenceIds = null)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A relationship cannot use an empty identifier.", nameof(id));
        }

        if (sourceId.IsEmpty)
        {
            throw new ArgumentException("A relationship cannot use an empty source identifier.", nameof(sourceId));
        }

        if (targetId.IsEmpty)
        {
            throw new ArgumentException("A relationship cannot use an empty target identifier.", nameof(targetId));
        }

        Id = id;
        Kind = IrGuard.NormalizeKind(kind, nameof(kind));
        SourceId = sourceId;
        TargetId = targetId;
        Confidence = confidence;
        EvidenceIds = IrGuard.CopyOrdered(evidenceIds, Comparer<StructuralId>.Default, distinct: true);
    }

    /// <summary>
    /// Gets the stable relationship fact identifier.
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
    /// Gets the analyzer confidence in this relationship.
    /// </summary>
    public ConfidenceScore Confidence { get; }

    /// <summary>
    /// Gets the deterministically ordered evidence identifiers supporting this relationship.
    /// </summary>
    public IReadOnlyList<StructuralId> EvidenceIds { get; }
}

namespace Scout.Core.Ir;

/// <summary>
/// Records provenance supporting one or more structural facts.
/// </summary>
public sealed class EvidenceFact
{
    /// <summary>
    /// Initializes a structural evidence fact.
    /// </summary>
    /// <param name="id">The stable evidence identifier.</param>
    /// <param name="kind">The extensible evidence kind.</param>
    /// <param name="analyzerId">The analyzer that produced the evidence.</param>
    /// <param name="description">A concise factual description of the observation.</param>
    /// <param name="confidence">The confidence in the observation.</param>
    /// <param name="locations">The source locations supporting the observation.</param>
    public EvidenceFact(
        StructuralId id,
        string kind,
        string analyzerId,
        string description,
        ConfidenceScore confidence,
        IEnumerable<SourceLocation>? locations = null)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Evidence cannot use an empty identifier.", nameof(id));
        }

        Id = id;
        Kind = IrGuard.NormalizeKind(kind, nameof(kind));
        AnalyzerId = IrGuard.NormalizeKind(analyzerId, nameof(analyzerId));
        Description = IrGuard.Required(description, nameof(description));
        Confidence = confidence;
        Locations = IrGuard.CopyOrdered(locations, SourceLocation.DeterministicComparer);
    }

    /// <summary>
    /// Gets the stable evidence identifier.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the extensible evidence kind.
    /// </summary>
    public string Kind { get; }

    /// <summary>
    /// Gets the stable identifier of the analyzer that produced the evidence.
    /// </summary>
    public string AnalyzerId { get; }

    /// <summary>
    /// Gets the concise factual description of the observation.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the confidence in the observation.
    /// </summary>
    public ConfidenceScore Confidence { get; }

    /// <summary>
    /// Gets the deterministically ordered source locations supporting the observation.
    /// </summary>
    public IReadOnlyList<SourceLocation> Locations { get; }
}

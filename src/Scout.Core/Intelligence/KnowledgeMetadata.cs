using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Carries the audit metadata common to every project-intelligence claim.
/// </summary>
public sealed class KnowledgeMetadata
{
    /// <summary>
    /// Initializes audit metadata for an intelligence claim.
    /// </summary>
    /// <param name="classification">Whether the claim is a fact, inference, or recommendation.</param>
    /// <param name="confidence">The normalized confidence in the claim.</param>
    /// <param name="scope">The portable repository scope to which the claim applies.</param>
    /// <param name="freshness">The estimated freshness of the claim.</param>
    /// <param name="evidenceIds">The evidence that supports the claim.</param>
    /// <param name="exceptionEntityIds">Known structural exceptions to the claim.</param>
    public KnowledgeMetadata(
        KnowledgeClassification classification,
        ConfidenceScore confidence,
        string scope,
        KnowledgeFreshness freshness,
        IEnumerable<StructuralId>? evidenceIds = null,
        IEnumerable<StructuralId>? exceptionEntityIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        Classification = classification;
        Confidence = confidence;
        Scope = scope.Trim().Replace('\\', '/');
        Freshness = freshness;
        EvidenceIds = CopyIds(evidenceIds);
        ExceptionEntityIds = CopyIds(exceptionEntityIds);
    }

    /// <summary>Gets whether the claim is a fact, inference, or recommendation.</summary>
    public KnowledgeClassification Classification { get; }

    /// <summary>Gets the normalized confidence in the claim.</summary>
    public ConfidenceScore Confidence { get; }

    /// <summary>Gets the portable repository scope to which the claim applies.</summary>
    public string Scope { get; }

    /// <summary>Gets the estimated freshness of the claim.</summary>
    public KnowledgeFreshness Freshness { get; }

    /// <summary>Gets the sorted evidence identifiers supporting the claim.</summary>
    public IReadOnlyList<StructuralId> EvidenceIds { get; }

    /// <summary>Gets the sorted structural entities that are known exceptions.</summary>
    public IReadOnlyList<StructuralId> ExceptionEntityIds { get; }

    private static ReadOnlyCollection<StructuralId> CopyIds(IEnumerable<StructuralId>? identifiers)
    {
        var values = identifiers?
            .Where(identifier => !identifier.IsEmpty)
            .Distinct()
            .Order()
            .ToArray() ?? [];
        return new ReadOnlyCollection<StructuralId>(values);
    }
}

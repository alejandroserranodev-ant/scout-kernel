using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Describes a scoped project convention inferred from repeated structural evidence.
/// </summary>
public sealed class ConventionIntelligence
{
    /// <summary>
    /// Initializes inferred convention intelligence.
    /// </summary>
    /// <param name="id">The stable intelligence identifier.</param>
    /// <param name="rule">A concise natural-language rule.</param>
    /// <param name="pattern">The observed machine-readable pattern.</param>
    /// <param name="support">The number of matching entities.</param>
    /// <param name="population">The number of candidate entities in scope.</param>
    /// <param name="metadata">Evidence and confidence metadata.</param>
    public ConventionIntelligence(
        StructuralId id,
        string rule,
        string pattern,
        int support,
        int population,
        KnowledgeMetadata metadata)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A convention identifier cannot be empty.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(rule);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (support < 1 || population < support)
        {
            throw new ArgumentOutOfRangeException(nameof(support), "Convention support must be positive and cannot exceed its population.");
        }

        Id = id;
        Rule = rule.Trim();
        Pattern = pattern.Trim();
        Support = support;
        Population = population;
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Gets the stable intelligence identifier.</summary>
    public StructuralId Id { get; }

    /// <summary>Gets the concise inferred rule.</summary>
    public string Rule { get; }

    /// <summary>Gets the observed machine-readable pattern.</summary>
    public string Pattern { get; }

    /// <summary>Gets the number of matching entities.</summary>
    public int Support { get; }

    /// <summary>Gets the number of candidate entities in scope.</summary>
    public int Population { get; }

    /// <summary>Gets evidence, confidence, scope, freshness, and classification.</summary>
    public KnowledgeMetadata Metadata { get; }
}

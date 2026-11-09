namespace Scout.Core.Retrieval;

/// <summary>
/// One compact, self-sufficient unit of kernel knowledge, written to be answered with rather than read around.
/// </summary>
/// <remarks>
/// A card is the reason retrieval saves tokens. It carries the navigation target, the consequence of touching
/// it, and the reference needed to go deeper — so an agent that receives a handful of cards can decide what to
/// open instead of opening files to find out.
/// </remarks>
public sealed class RetrievalCard
{
    /// <summary>
    /// Initializes a retrieval card.
    /// </summary>
    /// <param name="id">The identifier of the knowledge this card projects.</param>
    /// <param name="kind">The card kind. See <see cref="KnownCardKinds"/>.</param>
    /// <param name="name">The display name.</param>
    /// <param name="relativePath">The repository-relative navigation target.</param>
    /// <param name="summary">A compact statement of what this is and what depends on it.</param>
    /// <param name="importance">A non-negative prior used to break ties between equally similar cards.</param>
    /// <param name="facets">Compact attributes worth returning without opening the detail.</param>
    /// <param name="related">Names connected to this card, so a query naming a neighbour still finds it.</param>
    /// <param name="detail">The kernel-relative path holding the complete record, when one exists.</param>
    public RetrievalCard(
        string id,
        string kind,
        string name,
        string relativePath,
        string summary,
        double importance,
        IEnumerable<RetrievalFacet>? facets = null,
        IEnumerable<string>? related = null,
        string? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentOutOfRangeException.ThrowIfNegative(importance);

        Id = id;
        Kind = kind;
        Name = name.Trim();
        RelativePath = relativePath ?? string.Empty;
        Summary = summary.Trim();
        Importance = importance;
        Facets = facets?.ToArray() ?? [];
        Related = related?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray() ?? [];
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail;
        SearchText = BuildSearchText();
    }

    /// <summary>Gets the identifier of the knowledge this card projects.</summary>
    public string Id { get; }

    /// <summary>Gets the card kind.</summary>
    public string Kind { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the repository-relative navigation target.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the compact statement of what this is and what depends on it.</summary>
    public string Summary { get; }

    /// <summary>Gets the prior used to break ties between equally similar cards.</summary>
    public double Importance { get; }

    /// <summary>Gets the compact attributes returned without opening the detail.</summary>
    public IReadOnlyList<RetrievalFacet> Facets { get; }

    /// <summary>Gets the names connected to this card.</summary>
    public IReadOnlyList<string> Related { get; }

    /// <summary>Gets the kernel-relative path holding the complete record, when one exists.</summary>
    public string? Detail { get; }

    /// <summary>Gets the text this card is indexed by.</summary>
    public string SearchText { get; }

    // Neighbour names are indexed deliberately: an agent usually knows the symbol it is holding, not the
    // boundary that governs it, so naming a consumer has to be enough to surface the contract above it.
    private string BuildSearchText() =>
        string.Join(' ', new[] { Kind, Name, RelativePath, Summary }
            .Concat(Facets.Select(facet => facet.Value))
            .Concat(Related)
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}

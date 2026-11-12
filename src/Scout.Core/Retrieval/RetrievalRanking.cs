namespace Scout.Core.Retrieval;

/// <summary>
/// Orders candidate cards for a query.
/// </summary>
/// <remarks>
/// Similarity decides the ranking; the card's prior only tilts it. A stability boundary is more often the
/// useful answer than a private declaration that happens to share a word, but a clearly better textual match
/// still wins, because the prior can lift a card by at most a quarter of its own score.
/// </remarks>
public static class RetrievalRanking
{
    private const double ImportanceInfluence = 0.25;

    /// <summary>
    /// How much more a card whose name is exactly the query is worth. Cosine similarity over shared word parts can
    /// leave a card named "ModelHero" a hair behind one named "Model" for the query "ModelHero"; asking for a name
    /// is the clearest request there is, so the card with that name is never outranked by a near neighbour.
    /// </summary>
    public const double ExactNameMultiplier = 2d;

    // Rank fusion rewards a card for ranking well in either channel and rewards it most for ranking well in both.
    // The offset sets how fast a rank loses value: small enough that the top of a list matters, large enough that
    // the prior (at most a quarter of the score) moves a card a few places and not from the tenth to the first.
    private const double FusionOffset = 20d;

    /// <summary>How many cards each channel contributes to a fusion; ranks beyond it add nothing.</summary>
    public const int FusionDepth = 50;

    /// <summary>The smallest similarity worth returning, below which a match is noise.</summary>
    public const double MinimumSimilarity = 0.01;

    /// <summary>
    /// Applies a card's prior to its similarity. This is the single definition of rank score, so an index
    /// that scores in storage and one that scores in memory cannot drift apart.
    /// </summary>
    /// <param name="similarity">The cosine similarity between query and card.</param>
    /// <param name="importance">The card's non-negative prior.</param>
    /// <returns>The rank score.</returns>
    public static double Score(double similarity, double importance) =>
        similarity * (1d + (ImportanceInfluence * importance));

    /// <summary>
    /// Scores and orders candidates whose vectors are available.
    /// </summary>
    /// <param name="candidates">The candidate cards paired with their document vectors.</param>
    /// <param name="query">The embedded query.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <param name="exactName">The query text, so a card with exactly that name is ranked first.</param>
    /// <returns>The ranked hits, which is empty when nothing in the query is recognized.</returns>
    public static IReadOnlyList<RetrievalHit> Rank(
        IEnumerable<(RetrievalCard Card, EmbeddingVector Vector)> candidates,
        EmbeddingVector query,
        int limit,
        string? exactName = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(query);

        return query.IsEmpty
            ? []
            : Rank(
                candidates.Select(candidate => (candidate.Card, query.SimilarityTo(candidate.Vector))),
                limit,
                exactName);
    }

    /// <summary>
    /// Merges the lexical and the semantic ranking of one query into a single list.
    /// </summary>
    /// <remarks>
    /// The two channels measure different things on different scales, so they are combined by rank, not by value
    /// (reciprocal rank fusion). A name the query spells exactly is found by the lexical channel; a paraphrase or an
    /// abbreviation is found by the semantic one; a card that both agree on rises above one only a channel liked.
    /// The card's prior then tilts the result exactly as it does for a lexical ranking.
    /// </remarks>
    /// <param name="lexical">The lexical hits, best first.</param>
    /// <param name="semantic">The semantic candidates with their cosine similarity, best first.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <returns>The fused hits, highest score first, with ties broken deterministically by identifier. The score is
    /// scaled so a card first in both channels scores 1 before the prior.</returns>
    public static IReadOnlyList<RetrievalHit> Fuse(
        IReadOnlyList<RetrievalHit> lexical,
        IReadOnlyList<(RetrievalCard Card, double Similarity)> semantic,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        ArgumentNullException.ThrowIfNull(semantic);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var fused = new Dictionary<string, (RetrievalCard Card, double Fusion, double Similarity)>(StringComparer.Ordinal);
        for (var rank = 0; rank < Math.Min(lexical.Count, FusionDepth); rank++)
        {
            var hit = lexical[rank];
            fused[hit.Card.Id] = (hit.Card, 1d / (FusionOffset + rank + 1), hit.Similarity);
        }

        for (var rank = 0; rank < Math.Min(semantic.Count, FusionDepth); rank++)
        {
            var (card, similarity) = semantic[rank];
            var share = 1d / (FusionOffset + rank + 1);
            fused[card.Id] = fused.TryGetValue(card.Id, out var existing)
                ? (existing.Card, existing.Fusion + share, Math.Max(existing.Similarity, similarity))
                : (card, share, similarity);
        }

        var best = 2d / (FusionOffset + 1);
        return fused.Values
            .Select(entry => new RetrievalHit(entry.Card, Score(entry.Fusion / best, entry.Card.Importance), entry.Similarity))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Card.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static double ExactNameFactor(RetrievalCard card, string? exactName) =>
        !string.IsNullOrWhiteSpace(exactName) && string.Equals(card.Name, exactName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? ExactNameMultiplier
            : 1d;

    /// <summary>
    /// Orders candidates whose similarity was already computed, such as by an inverted index in storage.
    /// </summary>
    /// <param name="candidates">The candidate cards paired with their similarity to the query.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <param name="exactName">The query text, so a card with exactly that name is ranked first.</param>
    /// <returns>The hits, highest score first, with ties broken deterministically by identifier.</returns>
    public static IReadOnlyList<RetrievalHit> Rank(
        IEnumerable<(RetrievalCard Card, double Similarity)> candidates,
        int limit,
        string? exactName = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return candidates
            .Where(candidate => candidate.Similarity >= MinimumSimilarity)
            .Select(candidate => new RetrievalHit(
                candidate.Card,
                Score(candidate.Similarity, candidate.Card.Importance) * ExactNameFactor(candidate.Card, exactName),
                candidate.Similarity))
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => hit.Card.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }
}

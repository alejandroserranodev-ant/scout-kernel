namespace Scout.Core.Retrieval;

/// <summary>
/// A card that matched a query, with the score that placed it.
/// </summary>
/// <param name="Card">The matched card.</param>
/// <param name="Score">The rank score, combining similarity with the card's prior.</param>
/// <param name="Similarity">The raw cosine similarity, before any prior was applied.</param>
public sealed record RetrievalHit(RetrievalCard Card, double Score, double Similarity);

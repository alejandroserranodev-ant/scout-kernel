namespace Scout.Core.Retrieval;

/// <summary>
/// The answer to a query, with how it was produced.
/// </summary>
/// <param name="Hits">The ranked hits.</param>
/// <param name="Mode">
/// <see cref="KnownSearchModes.Hybrid"/> when lexical and semantic rankings were fused, otherwise
/// <see cref="KnownSearchModes.Lexical"/>.
/// </param>
/// <param name="Notice">
/// Why a semantic ranking was not used although the index carries one, or <see langword="null"/>. A caller shows
/// it, so a missing model is never mistaken for a query with nothing semantic to add.
/// </param>
public sealed record RetrievalSearchResult(IReadOnlyList<RetrievalHit> Hits, string Mode, string? Notice = null);

/// <summary>
/// The ways a query can be answered.
/// </summary>
public static class KnownSearchModes
{
    /// <summary>Answered from the inverted index alone: deterministic and available everywhere.</summary>
    public const string Lexical = "lexical";

    /// <summary>Answered by fusing the inverted index with the dense vectors of the index.</summary>
    public const string Hybrid = "hybrid";
}

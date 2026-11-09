namespace Scout.Core.Retrieval;

/// <summary>
/// What a vector space is able to match, stated so callers do not assume more than the model delivers.
/// </summary>
public static class KnownEmbeddingKinds
{
    /// <summary>
    /// Matches the words actually present in the indexed text, including the parts of identifiers.
    /// A query for wording the repository never uses will not match, however related it is in meaning.
    /// </summary>
    public const string Lexical = "lexical";

    /// <summary>
    /// Matches meaning rather than wording, so unrelated vocabulary describing the same concept matches.
    /// </summary>
    public const string Semantic = "semantic";
}

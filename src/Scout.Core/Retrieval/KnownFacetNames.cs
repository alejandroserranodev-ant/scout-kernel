namespace Scout.Core.Retrieval;

/// <summary>
/// Names of facets that carry meaning beyond "an attribute to show." See <see cref="InternalOnly"/>.
/// </summary>
public static class KnownFacetNames
{
    /// <summary>The symbol's one-based start line in its source file.</summary>
    public const string Line = "line";

    /// <summary>The symbol's one-based inclusive end line, letting a caller know where its declaration stops.</summary>
    public const string EndLine = "endLine";

    /// <summary>The source file's content hash at compile time.</summary>
    public const string ContentHash = "contentHash";

    /// <summary>The algorithm <see cref="ContentHash"/> was computed with.</summary>
    public const string HashAlgorithm = "hashAlgorithm";

    /// <summary>
    /// The distinguishing words a file uses (see <see cref="FileVocabulary"/>). Searched, but never returned: it
    /// exists so a query finds the file, and listing it would only spend an agent's tokens.
    /// </summary>
    public const string BodyTerms = "bodyTerms";

    /// <summary>
    /// Facet names that exist only so a resolver (<c>scout read</c>, <c>scout brief</c>) can turn a card into an
    /// exact source location and a staleness check, without reading the much larger structural IR. A hash or a
    /// line number is never what a question is phrased in, so these are excluded from a card's indexed search
    /// text and from a query's own rendered output.
    /// </summary>
    public static readonly IReadOnlySet<string> InternalOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        EndLine,
        ContentHash,
        HashAlgorithm
    };

    /// <summary>Facets that are never shown to a caller: <see cref="InternalOnly"/> plus <see cref="BodyTerms"/>.</summary>
    public static readonly IReadOnlySet<string> HiddenFromOutput = new HashSet<string>(StringComparer.Ordinal)
    {
        EndLine,
        ContentHash,
        HashAlgorithm,
        BodyTerms
    };
}

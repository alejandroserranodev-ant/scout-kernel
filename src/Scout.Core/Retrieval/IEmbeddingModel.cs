namespace Scout.Core.Retrieval;

/// <summary>
/// Turns arbitrary text into a vector in an already-established space.
/// </summary>
public interface IQueryEmbedder
{
    /// <summary>
    /// Embeds text into the space.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <returns>The unit-length vector, or <see cref="EmbeddingVector.Empty"/> when nothing is recognized.</returns>
    EmbeddingVector Embed(string? text);
}

/// <summary>
/// An established vector space: the document vectors it produced and the vocabulary needed to re-enter it.
/// </summary>
public interface IEmbeddingSpace : IQueryEmbedder
{
    /// <summary>Gets the document vectors, positionally aligned with the documents the space was built from.</summary>
    IReadOnlyList<EmbeddingVector> DocumentVectors { get; }

    /// <summary>
    /// Gets the vocabulary required to embed a query later, ordered by dimension. Models that carry their
    /// weights internally rather than in a vocabulary return an empty list.
    /// </summary>
    IReadOnlyList<EmbeddingTerm> Vocabulary { get; }
}

/// <summary>
/// Builds the vector space a Scout Kernel is retrieved through.
/// </summary>
/// <remarks>
/// This is the seam where retrieval quality can be improved without touching the kernel format, the index
/// schema, or the query command. An implementation must state honestly whether it is lexical or semantic,
/// because that determines what a caller may expect a query to match.
/// </remarks>
public interface IEmbeddingModel
{
    /// <summary>Gets the stable identifier persisted with the index, so a stale index can be detected.</summary>
    string ModelId { get; }

    /// <summary>Gets what the model actually matches. See <see cref="KnownEmbeddingKinds"/>.</summary>
    string Kind { get; }

    /// <summary>
    /// Builds a space from the retrieval corpus.
    /// </summary>
    /// <param name="documents">The document texts, in the order their cards are indexed.</param>
    /// <returns>The established space.</returns>
    IEmbeddingSpace Build(IReadOnlyList<string> documents);

    /// <summary>
    /// Re-enters a previously built space so a query can be embedded against a persisted index.
    /// </summary>
    /// <param name="vocabulary">The persisted vocabulary.</param>
    /// <returns>An embedder positioned in the same space.</returns>
    IQueryEmbedder Restore(IReadOnlyList<EmbeddingTerm> vocabulary);
}

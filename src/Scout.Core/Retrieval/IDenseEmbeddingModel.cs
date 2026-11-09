namespace Scout.Core.Retrieval;

/// <summary>
/// Turns text into dense semantic vectors, so two phrasings of one idea land near each other even when they
/// share no word.
/// </summary>
/// <remarks>
/// This is the second embedding port. <see cref="IEmbeddingModel"/> produces the sparse, deterministic vectors
/// the inverted index is built on; a dense model sits beside it and is opt-in, because it needs a model that the
/// default offline compile does not. A kernel records which dense model built its vectors, since a query is only
/// comparable with vectors from the same model.
/// </remarks>
public interface IDenseEmbeddingModel
{
    /// <summary>Gets the identifier of the model, stored with the vectors it produced.</summary>
    string ModelId { get; }

    /// <summary>
    /// Embeds texts. Implementations return exactly one vector per text, in order, all of the same dimension and
    /// each normalized to unit length, so similarity is a dot product.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>One normalized vector per text.</returns>
    /// <exception cref="DenseEmbeddingUnavailableException">The model could not be reached or used.</exception>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Signals that a dense embedding model cannot be used right now, with a message that says what to do about it.
/// </summary>
public sealed class DenseEmbeddingUnavailableException : Exception
{
    /// <summary>Initializes the exception.</summary>
    public DenseEmbeddingUnavailableException()
    {
    }

    /// <summary>Initializes the exception with a message.</summary>
    /// <param name="message">What is unavailable and how to fix it.</param>
    public DenseEmbeddingUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the exception with a message and its cause.</summary>
    /// <param name="message">What is unavailable and how to fix it.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DenseEmbeddingUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

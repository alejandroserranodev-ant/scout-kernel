namespace Scout.Core.Kernel;

/// <summary>
/// A retrieval index ready to be published inside a kernel.
/// </summary>
/// <param name="RelativePath">The kernel-relative path of the index.</param>
/// <param name="Format">The storage format identifier recorded in the manifest.</param>
/// <param name="ModelId">The embedding model the index was built with.</param>
/// <param name="CardCount">The number of retrievable cards.</param>
/// <param name="TermCount">The size of the vocabulary.</param>
/// <param name="Content">The serialized index.</param>
/// <param name="DenseModelId">The dense model whose vectors the index carries, or <see langword="null"/> when it has none.</param>
internal sealed record RetrievalPublication(
    string RelativePath,
    string Format,
    string ModelId,
    int CardCount,
    int TermCount,
    ReadOnlyMemory<byte> Content,
    string? DenseModelId = null);

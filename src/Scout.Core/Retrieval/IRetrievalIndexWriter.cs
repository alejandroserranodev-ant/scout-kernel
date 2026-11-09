namespace Scout.Core.Retrieval;

/// <summary>
/// Serializes a built retrieval index into the bytes published inside a Scout Kernel.
/// </summary>
/// <remarks>
/// The storage format is deliberately outside <c>Scout.Core</c>, exactly as language knowledge is. Core
/// decides what is worth retrieving and how it is scored; it does not decide what a database looks like.
/// </remarks>
public interface IRetrievalIndexWriter
{
    /// <summary>Gets the kernel-relative path the index is published at.</summary>
    string RelativePath { get; }

    /// <summary>Gets a short description of the storage format, recorded in the kernel manifest.</summary>
    string FormatDescription { get; }

    /// <summary>
    /// Serializes the index.
    /// </summary>
    /// <param name="index">The index to serialize.</param>
    /// <param name="sourceFingerprint">The fingerprint of the repository the index describes.</param>
    /// <param name="cancellationToken">A token that cancels serialization.</param>
    /// <returns>The bytes to publish.</returns>
    Task<ReadOnlyMemory<byte>> SerializeAsync(
        RetrievalIndex index,
        string? sourceFingerprint,
        CancellationToken cancellationToken = default);
}

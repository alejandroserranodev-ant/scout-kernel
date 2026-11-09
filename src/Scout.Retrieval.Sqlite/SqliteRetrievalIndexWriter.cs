using Scout.Core.Retrieval;

namespace Scout.Retrieval.Sqlite;

/// <summary>
/// Publishes a retrieval index as a SQLite database inside a Scout Kernel.
/// </summary>
public sealed class SqliteRetrievalIndexWriter : IRetrievalIndexWriter
{
    /// <inheritdoc />
    public string RelativePath => "retrieval/index.db";

    /// <inheritdoc />
    public string FormatDescription => "sqlite-inverted-index-1";

    /// <inheritdoc />
    /// <remarks>
    /// SQLite writes through the file system rather than to a buffer, so the database is built beside the
    /// process and handed back as bytes. That keeps kernel publication atomic: the kernel directory is only
    /// ever replaced once every artifact exists.
    /// </remarks>
    public async Task<ReadOnlyMemory<byte>> SerializeAsync(
        RetrievalIndex index,
        string? sourceFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);

        var stagingPath = Path.Combine(Path.GetTempPath(), $"scout-retrieval-{Guid.NewGuid():N}.db");
        try
        {
            SqliteRetrievalDatabase.Write(stagingPath, index, sourceFingerprint);
            return await File.ReadAllBytesAsync(stagingPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }
}

using System.Security.Cryptography;
using System.Text;

namespace Scout.Core.Retrieval;

/// <summary>
/// Stores dense vectors by the hash of the text that produced them, so a text is embedded once per model.
/// </summary>
public interface IDenseEmbeddingCache
{
    /// <summary>Reads the vectors already stored for a model.</summary>
    /// <param name="modelId">The model that produced the vectors.</param>
    /// <param name="textHashes">The hashes to look up.</param>
    /// <returns>The stored vectors, keyed by hash. Hashes that are not stored are absent.</returns>
    IReadOnlyDictionary<string, float[]> GetMany(string modelId, IReadOnlyCollection<string> textHashes);

    /// <summary>Stores vectors for a model.</summary>
    /// <param name="modelId">The model that produced the vectors.</param>
    /// <param name="vectors">The vectors, keyed by the hash of their text.</param>
    void PutMany(string modelId, IReadOnlyDictionary<string, float[]> vectors);
}

/// <summary>
/// Reuses stored vectors for texts that were already embedded, and embeds only the rest.
/// </summary>
/// <remarks>
/// A refresh changes a few cards out of thousands. Without this, every update would pay for the whole corpus,
/// which is what would make a semantic index too slow to keep current.
/// </remarks>
public sealed class CachedDenseEmbeddingModel : IDenseEmbeddingModel
{
    // Vectors are stored after every chunk, so an interrupted run keeps what it already paid for and the next
    // run resumes instead of starting over.
    private const int ChunkSize = 128;

    private readonly IDenseEmbeddingModel inner;
    private readonly IDenseEmbeddingCache cache;
    private readonly Action<int, int>? progress;

    /// <summary>
    /// Initializes the caching decorator.
    /// </summary>
    /// <param name="inner">The model that embeds texts the cache does not hold.</param>
    /// <param name="cache">The cache.</param>
    /// <param name="progress">
    /// Optional. Called with the number of texts embedded so far and the number that must be, after each chunk, so a
    /// long first run can show that it is working.
    /// </param>
    public CachedDenseEmbeddingModel(
        IDenseEmbeddingModel inner,
        IDenseEmbeddingCache cache,
        Action<int, int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(cache);
        this.inner = inner;
        this.cache = cache;
        this.progress = progress;
    }

    /// <inheritdoc />
    public string ModelId => inner.ModelId;

    /// <summary>Gets how many texts of the last request were served from the cache.</summary>
    public int Reused { get; private set; }

    /// <summary>Gets how many texts of the last request had to be embedded.</summary>
    public int Embedded { get; private set; }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var hashes = texts.Select(Hash).ToArray();
        var stored = cache.GetMany(inner.ModelId, hashes.Distinct(StringComparer.Ordinal).ToArray());

        var missing = new List<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < texts.Count; index++)
        {
            if (!stored.ContainsKey(hashes[index]) && seen.Add(hashes[index]))
            {
                missing.Add(index);
            }
        }

        var computed = new Dictionary<string, float[]>(StringComparer.Ordinal);
        for (var start = 0; start < missing.Count; start += ChunkSize)
        {
            var chunk = missing.Skip(start).Take(ChunkSize).ToArray();
            var vectors = await inner
                .EmbedAsync(chunk.Select(index => texts[index]).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            if (vectors.Count != chunk.Length)
            {
                throw new InvalidOperationException("A dense embedding model must return exactly one vector per text.");
            }

            var chunkVectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
            for (var position = 0; position < chunk.Length; position++)
            {
                chunkVectors[hashes[chunk[position]]] = vectors[position];
                computed[hashes[chunk[position]]] = vectors[position];
            }

            cache.PutMany(inner.ModelId, chunkVectors);
            progress?.Invoke(Math.Min(start + chunk.Length, missing.Count), missing.Count);
        }

        Embedded = missing.Count;
        Reused = texts.Count - missing.Count;
        return hashes.Select(hash => stored.TryGetValue(hash, out var vector) ? vector : computed[hash]).ToArray();
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

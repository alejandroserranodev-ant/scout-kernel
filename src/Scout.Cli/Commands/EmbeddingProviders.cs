using Scout.Core.Retrieval;
using Scout.Embeddings.Ollama;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Composes dense embedding models from the text that names them.
/// </summary>
/// <remarks>
/// The command line is the composition root, so this is the one place that knows which providers exist. A provider
/// is chosen by name when a kernel is built and recovered from the kernel's own record when it is queried.
/// </remarks>
internal static class EmbeddingProviders
{
    internal const string Usage = "ollama[:<model>]";

    /// <summary>
    /// Builds the model used to add semantic vectors to a kernel, reusing vectors already computed on this machine.
    /// </summary>
    /// <param name="specification">The provider named on the command line.</param>
    /// <param name="progress">Where to report how far a long first embedding run has got.</param>
    /// <returns>The model, or <see langword="null"/> when no provider has that name.</returns>
    internal static CachedDenseEmbeddingModel? ForCompile(string specification, TextWriter progress)
    {
        var model = OllamaEmbeddingModel.TryParse(specification);
        if (model is null)
        {
            return null;
        }

        var cache = new SqliteDenseEmbeddingCache(SqliteDenseEmbeddingCache.DefaultPath());
        var lastReported = 0;
        return new CachedDenseEmbeddingModel(
            model,
            cache,
            (done, total) =>
            {
                // A first run embeds thousands of texts, minutes on a machine without a GPU: say it is working.
                if (done == total || done - lastReported >= 512)
                {
                    lastReported = done;
                    progress.WriteLine($"Embedding retrieval cards with '{model.ModelId}': {done}/{total}");
                }
            });
    }

    /// <summary>
    /// Recovers the model that built an index's semantic vectors, so a query can be embedded into the same space.
    /// </summary>
    /// <param name="modelId">The dense model identifier recorded in the index.</param>
    /// <returns>The model, or <see langword="null"/> when this build of Scout does not know the provider.</returns>
    internal static IDenseEmbeddingModel? ForIndex(string modelId) => OllamaEmbeddingModel.TryParse(modelId);
}

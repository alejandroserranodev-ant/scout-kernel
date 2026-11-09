using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Repository;
using Scout.Core.Retrieval;

namespace Scout.Core.Kernel;

/// <summary>
/// Projects compiled facts and intelligence into a compact, atomically published Scout Kernel.
/// </summary>
public static class ScoutKernelGenerator
{
    /// <summary>
    /// Generates a Scout Kernel without performing additional source analysis or inference.
    /// </summary>
    /// <param name="repository">The discovered repository metadata.</param>
    /// <param name="structural">The structural source of truth.</param>
    /// <param name="intelligence">The intelligence previously derived from the structural graph.</param>
    /// <param name="outputPath">The kernel output directory.</param>
    /// <param name="scoutVersion">The Scout compiler version.</param>
    /// <param name="indexWriter">
    /// Serializes the retrieval index. When omitted, no index is published and the kernel is navigated by
    /// reading its index files, which costs the agent more tokens.
    /// </param>
    /// <param name="denseModel">
    /// An optional dense embedding model. When supplied, every card also gets a semantic vector and the index
    /// can answer paraphrases and abbreviations; when omitted the kernel is lexical and needs no model at all.
    /// </param>
    /// <param name="cancellationToken">A token that cancels generation before publication.</param>
    /// <returns>The generated kernel location and artifact catalog.</returns>
    public static async Task<KernelGenerationResult> GenerateAsync(
        RepositoryDescriptor repository,
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        string outputPath,
        string scoutVersion,
        IRetrievalIndexWriter? indexWriter = null,
        IDenseEmbeddingModel? denseModel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scoutVersion);

        var fullOutputPath = Path.GetFullPath(outputPath);
        if (PathsEqual(fullOutputPath, repository.RootPath))
        {
            throw new InvalidOperationException("The Scout Kernel cannot replace the source repository.");
        }

        var retrieval = indexWriter is null
            ? null
            : await BuildRetrievalAsync(structural, intelligence, repository, indexWriter, denseModel, cancellationToken)
                .ConfigureAwait(false);

        var artifacts = KernelRenderer.Render(repository, structural, intelligence, scoutVersion, retrieval);
        await KernelFilePublisher.PublishAsync(fullOutputPath, artifacts, cancellationToken).ConfigureAwait(false);
        return new KernelGenerationResult(
            fullOutputPath,
            artifacts.Select(artifact => artifact.RelativePath).ToArray());
    }

    /// <summary>
    /// Refreshes an existing Scout Kernel in place, creating it when it does not yet exist.
    /// </summary>
    /// <remarks>
    /// This is the pipeline-friendly entry point for CI and scheduled refreshes: the output directory is
    /// replaced atomically when it already exists, so the agent still sees a valid kernel during the swap.
    /// </remarks>
    public static Task<KernelGenerationResult> UpdateAsync(
        RepositoryDescriptor repository,
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        string outputPath,
        string scoutVersion,
        IRetrievalIndexWriter? indexWriter = null,
        IDenseEmbeddingModel? denseModel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scoutVersion);

        return GenerateAsync(
            repository,
            structural,
            intelligence,
            outputPath,
            scoutVersion,
            indexWriter,
            denseModel,
            cancellationToken);
    }

    private static async Task<RetrievalPublication> BuildRetrievalAsync(
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        RepositoryDescriptor repository,
        IRetrievalIndexWriter indexWriter,
        IDenseEmbeddingModel? denseModel,
        CancellationToken cancellationToken)
    {
        var cards = RetrievalCardCompiler.Compile(structural, intelligence);
        var index = RetrievalIndex.Build(cards, new LexicalEmbeddingModel());
        if (denseModel is not null)
        {
            var vectors = await denseModel
                .EmbedAsync(cards.Select(DenseText.Build).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            index = index.WithDense(new DenseIndex(denseModel.ModelId, vectors));
        }

        var content = await indexWriter
            .SerializeAsync(index, repository.ContentFingerprint, cancellationToken)
            .ConfigureAwait(false);

        return new RetrievalPublication(
            indexWriter.RelativePath,
            indexWriter.FormatDescription,
            index.ModelId,
            cards.Count,
            index.Vocabulary.Count,
            content,
            index.Dense?.ModelId);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}

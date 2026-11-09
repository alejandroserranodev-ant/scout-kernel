using Scout.Core.Repository;

namespace Scout.Core.Analysis;

/// <summary>
/// Analyzes one source language and contributes language-independent structural facts.
/// </summary>
public interface ILanguageAnalyzer
{
    /// <summary>
    /// Gets the stable analyzer identifier recorded in evidence and diagnostics.
    /// </summary>
    string AnalyzerId { get; }

    /// <summary>
    /// Gets the language identifier understood by this analyzer.
    /// </summary>
    string LanguageId { get; }

    /// <summary>
    /// Gets the repository input extensions required by this analyzer.
    /// </summary>
    IReadOnlyList<string> SupportedFileExtensions { get; }

    /// <summary>
    /// Analyzes a discovered repository and returns structural facts without analyzer-native objects.
    /// </summary>
    /// <param name="repository">The deterministic repository discovery result.</param>
    /// <param name="cancellationToken">A token that cancels analysis.</param>
    /// <returns>The analyzer contribution.</returns>
    Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default);
}

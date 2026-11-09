using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.CSharp;

/// <summary>
/// Extracts language-independent structural facts from restored SDK-style C# projects with Roslyn.
/// </summary>
public sealed class CSharpLanguageAnalyzer : ILanguageAnalyzer
{
    private const string PositionEncoding = "utf-16-code-units";
    private static readonly IReadOnlyList<string> Extensions = new ReadOnlyCollection<string>(
        [".cs", ".csproj", ".sln", ".slnx"]);
    private static readonly string AnalyzerVersion =
        typeof(CSharpLanguageAnalyzer).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Gets the stable identifier used by the C# analyzer.
    /// </summary>
    public string AnalyzerId => "scout.analysis.csharp";

    /// <summary>
    /// Gets the C# language identifier.
    /// </summary>
    public string LanguageId => "csharp";

    /// <summary>
    /// Gets the repository extensions needed for C# project and source discovery.
    /// </summary>
    public IReadOnlyList<string> SupportedFileExtensions => Extensions;

    /// <summary>
    /// Analyzes restored C# projects and returns only language-independent structural facts.
    /// </summary>
    /// <param name="repository">The deterministic repository discovery result.</param>
    /// <param name="cancellationToken">A token that cancels analysis.</param>
    /// <returns>The C# analyzer contribution.</returns>
    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var registration = MsBuildRegistration.EnsureRegistered(repository.RootPath);
            return await AnalyzeAfterRegistrationAsync(repository, registration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateFailureResult(repository.RootPath, exception.Message);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task<LanguageAnalysisResult> AnalyzeAfterRegistrationAsync(
        RepositoryDescriptor repository,
        MsBuildRegistrationResult registration,
        CancellationToken cancellationToken)
    {
        var extractor = new CSharpFactExtractor(
            repository,
            AnalyzerVersion,
            PositionEncoding,
            registration);
        return extractor.AnalyzeAsync(cancellationToken);
    }

    private static LanguageAnalysisResult CreateFailureResult(string repositoryRoot, string message)
    {
        var normalizedMessage = DiagnosticAccumulator.NormalizeMessage(repositoryRoot, message);
        var diagnostic = new AnalysisDiagnostic(
            StructuralId.Create("diagnostic", "scout.analysis.csharp", "CSHARP_ANALYZER_FAILURE", normalizedMessage),
            "scout.analysis.csharp",
            KnownDiagnosticSeverities.Error,
            "CSHARP_ANALYZER_FAILURE",
            normalizedMessage);
        var metadata = new AnalyzerRunMetadata(
            "scout.analysis.csharp",
            "Scout C# Analyzer",
            AnalyzerVersion,
            "csharp",
            KnownAnalyzerStatuses.Failed,
            DateTimeOffset.UtcNow,
            PositionEncoding);

        return new LanguageAnalysisResult(metadata, diagnostics: [diagnostic]);
    }
}

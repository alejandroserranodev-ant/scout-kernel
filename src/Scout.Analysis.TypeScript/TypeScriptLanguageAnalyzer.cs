using System.Collections.ObjectModel;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.TypeScript;

/// <summary>
/// Extracts language-independent structural facts from TypeScript, JavaScript and React sources.
/// </summary>
/// <remarks>
/// Analysis runs through the official TypeScript compiler in a Node sidecar, so declarations, heritage and
/// references are resolved by the same checker the repository compiles with. When the sidecar or Node is
/// unavailable the analyzer reports a diagnostic and contributes no facts instead of degrading to guesses.
/// </remarks>
public sealed class TypeScriptLanguageAnalyzer : ILanguageAnalyzer
{
    private const string PositionEncoding = "utf-16-code-units";
    private const string Id = "scout.analysis.typescript";

    private static readonly IReadOnlyList<string> Extensions = new ReadOnlyCollection<string>(
        [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"]);

    private static readonly string AnalyzerVersion =
        typeof(TypeScriptLanguageAnalyzer).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Gets the stable identifier used by the TypeScript analyzer.
    /// </summary>
    public string AnalyzerId => Id;

    /// <summary>
    /// Gets the TypeScript language identifier.
    /// </summary>
    public string LanguageId => "typescript";

    /// <summary>
    /// Gets the repository extensions the TypeScript analyzer reads.
    /// </summary>
    public IReadOnlyList<string> SupportedFileExtensions => Extensions;

    /// <summary>
    /// Analyzes TypeScript, JavaScript and React sources and returns only language-independent facts.
    /// </summary>
    /// <param name="repository">The deterministic repository discovery result.</param>
    /// <param name="cancellationToken">A token that cancels analysis.</param>
    /// <returns>The TypeScript analyzer contribution.</returns>
    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();

        var analyzedFiles = repository.Files
            .Select(file => file.RelativePath)
            .Where(IsAnalyzable)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (analyzedFiles.Length == 0)
        {
            return CreateEmptyResult(KnownAnalyzerStatuses.Succeeded);
        }

        var scriptPath = TypeScriptSidecar.FindScriptPath();
        if (scriptPath is null)
        {
            return CreateFailure(
                "TS_SIDECAR_MISSING",
                "The TypeScript sidecar was not published beside the analyzer assembly.");
        }

        SidecarResponse response;
        try
        {
            response = await TypeScriptSidecar
                .RunAsync(scriptPath, repository.RootPath, analyzedFiles, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateFailure("TS_ANALYZER_FAILURE", exception.Message);
        }

        var status = response.Status switch
        {
            "succeeded" => KnownAnalyzerStatuses.Succeeded,
            "partial" => KnownAnalyzerStatuses.Partial,
            _ => KnownAnalyzerStatuses.Failed
        };

        return new SidecarFactMapper(repository, Id).Map(response, CreateMetadata(status));
    }

    private static bool IsAnalyzable(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) &&
               !relativePath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);
    }

    private static AnalyzerRunMetadata CreateMetadata(string status)
    {
        return new AnalyzerRunMetadata(
            Id,
            "Scout TypeScript Analyzer",
            AnalyzerVersion,
            "typescript",
            status,
            DateTimeOffset.UtcNow,
            PositionEncoding);
    }

    private static LanguageAnalysisResult CreateEmptyResult(string status)
    {
        return new LanguageAnalysisResult(CreateMetadata(status));
    }

    private static LanguageAnalysisResult CreateFailure(string code, string message)
    {
        var diagnostic = new AnalysisDiagnostic(
            StructuralId.Create("diagnostic", Id, code, message),
            Id,
            KnownDiagnosticSeverities.Error,
            code,
            message);
        return new LanguageAnalysisResult(CreateMetadata(KnownAnalyzerStatuses.Failed), diagnostics: [diagnostic]);
    }
}

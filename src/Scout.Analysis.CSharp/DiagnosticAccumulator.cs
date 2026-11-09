using System.Globalization;
using Microsoft.CodeAnalysis;
using Scout.Core.Ir;

namespace Scout.Analysis.CSharp;

internal sealed class DiagnosticAccumulator
{
    private readonly string repositoryRoot;
    private readonly Dictionary<StructuralId, AnalysisDiagnostic> diagnostics = [];

    public DiagnosticAccumulator(string repositoryRoot)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
    }

    public bool HasCompletenessFailure { get; private set; }

    public IReadOnlyList<AnalysisDiagnostic> ToFacts() => diagnostics.Values
        .OrderBy(static diagnostic => diagnostic.Id)
        .ToArray();

    public void AddWorkspaceDiagnostic(WorkspaceDiagnostic diagnostic)
    {
        var isFailure = diagnostic.Kind == WorkspaceDiagnosticKind.Failure;
        Add(
            isFailure ? "CSHARP_WORKSPACE_FAILURE" : "CSHARP_WORKSPACE_WARNING",
            isFailure ? KnownDiagnosticSeverities.Error : KnownDiagnosticSeverities.Warning,
            diagnostic.Message,
            location: null,
            affectsCompleteness: true);
    }

    public void AddCompilerDiagnostic(
        Diagnostic diagnostic,
        Func<Location, SourceLocation?> locationFactory)
    {
        if (diagnostic.Severity == DiagnosticSeverity.Hidden)
        {
            return;
        }

        var severity = diagnostic.Severity switch
        {
            DiagnosticSeverity.Error => KnownDiagnosticSeverities.Error,
            DiagnosticSeverity.Warning => KnownDiagnosticSeverities.Warning,
            _ => KnownDiagnosticSeverities.Information
        };
        var location = diagnostic.Location is { IsInSource: true }
            ? locationFactory(diagnostic.Location)
            : null;

        Add(
            diagnostic.Id,
            severity,
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            location,
            affectsCompleteness: diagnostic.Severity == DiagnosticSeverity.Error);
    }

    public void Add(
        string code,
        string severity,
        string message,
        SourceLocation? location = null,
        bool affectsCompleteness = false)
    {
        var normalizedMessage = NormalizeMessage(repositoryRoot, message);
        var locationIdentity = location is null
            ? "no-location"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{location.RelativePath}:{location.StartLine}:{location.StartColumn}:{location.EndLine}:{location.EndColumn}");
        var id = StructuralId.Create(
            "diagnostic",
            CSharpLanguageAnalyzerId.Value,
            code,
            severity,
            normalizedMessage,
            locationIdentity);

        diagnostics.TryAdd(
            id,
            new AnalysisDiagnostic(
                id,
                CSharpLanguageAnalyzerId.Value,
                severity,
                code,
                normalizedMessage,
                location));
        HasCompletenessFailure |= affectsCompleteness;
    }

    public static string NormalizeMessage(string repositoryRoot, string message)
    {
        var normalized = string.IsNullOrWhiteSpace(message) ? "Unknown analyzer failure." : message.Trim();
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        normalized = normalized.Replace(fullRoot, ".", comparison);
        normalized = normalized.Replace(fullRoot.Replace('\\', '/'), ".", comparison);
        return normalized;
    }
}

internal static class CSharpLanguageAnalyzerId
{
    public const string Value = "scout.analysis.csharp";
}

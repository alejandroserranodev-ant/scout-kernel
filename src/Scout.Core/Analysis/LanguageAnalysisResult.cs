using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Analysis;

/// <summary>
/// Contains one language analyzer's deterministic contribution to a structural snapshot.
/// </summary>
public sealed class LanguageAnalysisResult
{
    /// <summary>
    /// Initializes an analyzer contribution and orders every fact collection deterministically.
    /// </summary>
    /// <param name="analyzer">Metadata describing the analyzer run.</param>
    /// <param name="projects">The observed source projects.</param>
    /// <param name="sourceFiles">The observed repository source files.</param>
    /// <param name="symbols">The observed source symbols.</param>
    /// <param name="relationships">The observed directed relationships.</param>
    /// <param name="evidence">The evidence supporting the facts.</param>
    /// <param name="diagnostics">Diagnostics describing incomplete or unknown analysis.</param>
    public LanguageAnalysisResult(
        AnalyzerRunMetadata analyzer,
        IEnumerable<ProjectFact>? projects = null,
        IEnumerable<SourceFileFact>? sourceFiles = null,
        IEnumerable<SymbolFact>? symbols = null,
        IEnumerable<RelationshipFact>? relationships = null,
        IEnumerable<EvidenceFact>? evidence = null,
        IEnumerable<AnalysisDiagnostic>? diagnostics = null)
    {
        Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        Projects = CopyAndSort(projects, static fact => fact.Id.Value);
        SourceFiles = CopyAndSort(sourceFiles, static fact => fact.Id.Value);
        Symbols = CopyAndSort(symbols, static fact => fact.Id.Value);
        Relationships = CopyAndSort(relationships, static fact => fact.Id.Value);
        Evidence = CopyAndSort(evidence, static fact => fact.Id.Value);
        Diagnostics = CopyAndSort(diagnostics, static fact => fact.Id.Value);
    }

    /// <summary>
    /// Gets metadata describing the analyzer run.
    /// </summary>
    public AnalyzerRunMetadata Analyzer { get; }

    /// <summary>
    /// Gets the deterministically ordered source projects.
    /// </summary>
    public IReadOnlyList<ProjectFact> Projects { get; }

    /// <summary>
    /// Gets the deterministically ordered source files.
    /// </summary>
    public IReadOnlyList<SourceFileFact> SourceFiles { get; }

    /// <summary>
    /// Gets the deterministically ordered source symbols.
    /// </summary>
    public IReadOnlyList<SymbolFact> Symbols { get; }

    /// <summary>
    /// Gets the deterministically ordered relationships.
    /// </summary>
    public IReadOnlyList<RelationshipFact> Relationships { get; }

    /// <summary>
    /// Gets the deterministically ordered evidence.
    /// </summary>
    public IReadOnlyList<EvidenceFact> Evidence { get; }

    /// <summary>
    /// Gets the deterministically ordered diagnostics.
    /// </summary>
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    private static ReadOnlyCollection<T> CopyAndSort<T>(IEnumerable<T>? values, Func<T, string> keySelector)
    {
        var sorted = values?.OrderBy(keySelector, StringComparer.Ordinal).ToArray() ?? [];
        return new ReadOnlyCollection<T>(sorted);
    }
}

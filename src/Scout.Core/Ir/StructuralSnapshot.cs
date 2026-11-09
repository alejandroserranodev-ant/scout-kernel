using System.Collections.ObjectModel;

namespace Scout.Core.Ir;

/// <summary>
/// Captures a deterministic, language-independent set of observed structural facts for one source revision.
/// </summary>
public sealed class StructuralSnapshot
{
    /// <summary>
    /// Gets the schema version emitted by this implementation.
    /// </summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>
    /// Initializes a structural snapshot and deterministically orders every fact collection.
    /// </summary>
    /// <param name="source">The source repository identity.</param>
    /// <param name="createdAtUtc">The snapshot creation timestamp.</param>
    /// <param name="analyzers">The analyzer runs that contributed facts.</param>
    /// <param name="projects">The observed source projects.</param>
    /// <param name="sourceFiles">The observed source files.</param>
    /// <param name="symbols">The observed source symbols.</param>
    /// <param name="relationships">The observed directed relationships.</param>
    /// <param name="evidence">The evidence supporting structural facts.</param>
    /// <param name="diagnostics">The diagnostics describing unknown or incomplete analysis.</param>
    /// <param name="schemaVersion">The persisted structural schema version.</param>
    public StructuralSnapshot(
        SourceIdentity source,
        DateTimeOffset createdAtUtc,
        IEnumerable<AnalyzerRunMetadata>? analyzers = null,
        IEnumerable<ProjectFact>? projects = null,
        IEnumerable<SourceFileFact>? sourceFiles = null,
        IEnumerable<SymbolFact>? symbols = null,
        IEnumerable<RelationshipFact>? relationships = null,
        IEnumerable<EvidenceFact>? evidence = null,
        IEnumerable<AnalysisDiagnostic>? diagnostics = null,
        string schemaVersion = CurrentSchemaVersion)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        SchemaVersion = IrGuard.Required(schemaVersion, nameof(schemaVersion));
        Analyzers = CopyAndSort(analyzers, static analyzer => analyzer.AnalyzerId);
        Projects = CopyAndSort(projects, static project => project.Id.Value);
        SourceFiles = CopyAndSort(sourceFiles, static sourceFile => sourceFile.Id.Value);
        Symbols = CopyAndSort(symbols, static symbol => symbol.Id.Value);
        Relationships = CopyAndSort(
            relationships,
            static relationship => $"{relationship.SourceId.Value}\u001f{relationship.TargetId.Value}\u001f{relationship.Kind}\u001f{relationship.Id.Value}");
        Evidence = CopyAndSort(evidence, static item => item.Id.Value);
        Diagnostics = CopyAndSort(
            diagnostics,
            static diagnostic => $"{diagnostic.AnalyzerId}\u001f{diagnostic.Code}\u001f{diagnostic.Id.Value}");
    }

    /// <summary>
    /// Gets the persisted structural schema version.
    /// </summary>
    public string SchemaVersion { get; }

    /// <summary>
    /// Gets the identity of the source repository revision.
    /// </summary>
    public SourceIdentity Source { get; }

    /// <summary>
    /// Gets the UTC snapshot creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the deterministically ordered analyzer run metadata.
    /// </summary>
    public IReadOnlyList<AnalyzerRunMetadata> Analyzers { get; }

    /// <summary>
    /// Gets the deterministically ordered project facts.
    /// </summary>
    public IReadOnlyList<ProjectFact> Projects { get; }

    /// <summary>
    /// Gets the deterministically ordered source file facts.
    /// </summary>
    public IReadOnlyList<SourceFileFact> SourceFiles { get; }

    /// <summary>
    /// Gets the deterministically ordered source symbol facts.
    /// </summary>
    public IReadOnlyList<SymbolFact> Symbols { get; }

    /// <summary>
    /// Gets the deterministically ordered relationship facts.
    /// </summary>
    public IReadOnlyList<RelationshipFact> Relationships { get; }

    /// <summary>
    /// Gets the deterministically ordered evidence facts.
    /// </summary>
    public IReadOnlyList<EvidenceFact> Evidence { get; }

    /// <summary>
    /// Gets the deterministically ordered analysis diagnostics.
    /// </summary>
    public IReadOnlyList<AnalysisDiagnostic> Diagnostics { get; }

    private static ReadOnlyCollection<T> CopyAndSort<T>(IEnumerable<T>? values, Func<T, string> keySelector)
    {
        var array = values?.OrderBy(keySelector, StringComparer.Ordinal).ToArray() ?? [];
        return new ReadOnlyCollection<T>(array);
    }
}

using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Represents the initial, explicitly inferred projection of a source project as a navigable module.
/// </summary>
public sealed class ModuleIntelligence
{
    /// <summary>
    /// Initializes inferred module intelligence.
    /// </summary>
    /// <param name="id">The stable intelligence identifier.</param>
    /// <param name="name">The module display name.</param>
    /// <param name="summary">A compact module summary.</param>
    /// <param name="sourceProjectId">The observed source project from which the module was inferred.</param>
    /// <param name="relativePath">The source project metadata path.</param>
    /// <param name="sourceFileCount">The observed source file count.</param>
    /// <param name="symbolCount">The observed symbol count.</param>
    /// <param name="dependencyModuleIds">The inferred modules on which this module depends.</param>
    /// <param name="metadata">Evidence and confidence metadata.</param>
    public ModuleIntelligence(
        StructuralId id,
        string name,
        string summary,
        StructuralId sourceProjectId,
        string relativePath,
        int sourceFileCount,
        int symbolCount,
        IEnumerable<StructuralId>? dependencyModuleIds,
        KnowledgeMetadata metadata)
    {
        if (id.IsEmpty || sourceProjectId.IsEmpty)
        {
            throw new ArgumentException("Module and source project identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (sourceFileCount < 0 || symbolCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceFileCount), "Module counts cannot be negative.");
        }

        Id = id;
        Name = name.Trim();
        Summary = summary.Trim();
        SourceProjectId = sourceProjectId;
        RelativePath = Ir.RepositoryPath.Normalize(relativePath);
        SourceFileCount = sourceFileCount;
        SymbolCount = symbolCount;
        DependencyModuleIds = new ReadOnlyCollection<StructuralId>(
            dependencyModuleIds?.Distinct().Order().ToArray() ?? []);
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Gets the stable intelligence identifier.</summary>
    public StructuralId Id { get; }

    /// <summary>Gets the module display name.</summary>
    public string Name { get; }

    /// <summary>Gets the compact module summary.</summary>
    public string Summary { get; }

    /// <summary>Gets the source project from which this module was inferred.</summary>
    public StructuralId SourceProjectId { get; }

    /// <summary>Gets the repository-relative source project metadata path.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the observed source file count.</summary>
    public int SourceFileCount { get; }

    /// <summary>Gets the observed symbol count.</summary>
    public int SymbolCount { get; }

    /// <summary>Gets the inferred module dependencies.</summary>
    public IReadOnlyList<StructuralId> DependencyModuleIds { get; }

    /// <summary>Gets evidence, confidence, scope, freshness, and classification.</summary>
    public KnowledgeMetadata Metadata { get; }
}

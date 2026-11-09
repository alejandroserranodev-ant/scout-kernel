using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Describes a symbol that observed usage establishes as a stability boundary, together with what breaks when it changes.
/// </summary>
/// <remarks>
/// A contract is an inference. It states that a symbol currently has consumers that depend on its shape, not that the
/// symbol is a supported or intentional public API.
/// </remarks>
public sealed class ContractIntelligence
{
    /// <summary>
    /// Initializes inferred contract intelligence.
    /// </summary>
    /// <param name="id">The stable intelligence identifier.</param>
    /// <param name="symbolId">The structural symbol that acts as the boundary.</param>
    /// <param name="name">The contract display name.</param>
    /// <param name="summary">A compact, factual summary of the observed boundary.</param>
    /// <param name="contractKind">The extensible reason the symbol behaves as a boundary.</param>
    /// <param name="symbolKind">The language-neutral kind of the underlying symbol.</param>
    /// <param name="visibility">The observed declared visibility of the underlying symbol.</param>
    /// <param name="qualifiedName">The optional qualified symbol name.</param>
    /// <param name="signature">The optional stable semantic signature.</param>
    /// <param name="declaringModuleId">The owning module, or <see langword="null"/> when ownership is unknown.</param>
    /// <param name="relativePath">The repository-relative declaration path used for navigation.</param>
    /// <param name="consumers">The observed consumers that depend on this boundary.</param>
    /// <param name="metadata">Evidence and confidence metadata.</param>
    public ContractIntelligence(
        StructuralId id,
        StructuralId symbolId,
        string name,
        string summary,
        string contractKind,
        string symbolKind,
        string visibility,
        string? qualifiedName,
        string? signature,
        StructuralId? declaringModuleId,
        string relativePath,
        IEnumerable<ContractConsumer> consumers,
        KnowledgeMetadata metadata)
    {
        if (id.IsEmpty || symbolId.IsEmpty)
        {
            throw new ArgumentException("Contract and symbol identifiers cannot be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentNullException.ThrowIfNull(consumers);

        var orderedConsumers = consumers
            .DistinctBy(consumer => consumer.SymbolId)
            .OrderBy(consumer => consumer.SymbolId)
            .ToArray();
        if (orderedConsumers.Length == 0)
        {
            throw new ArgumentException(
                "A contract requires at least one observed consumer; otherwise it is not evidence-backed.",
                nameof(consumers));
        }

        Id = id;
        SymbolId = symbolId;
        Name = name.Trim();
        Summary = summary.Trim();
        ContractKind = IrGuard.NormalizeKind(contractKind, nameof(contractKind));
        SymbolKind = IrGuard.NormalizeKind(symbolKind, nameof(symbolKind));
        Visibility = IrGuard.NormalizeKind(visibility, nameof(visibility));
        QualifiedName = string.IsNullOrWhiteSpace(qualifiedName) ? null : qualifiedName.Trim();
        Signature = string.IsNullOrWhiteSpace(signature) ? null : signature.Trim();
        DeclaringModuleId = declaringModuleId is { IsEmpty: false } ? declaringModuleId : null;
        RelativePath = Ir.RepositoryPath.Normalize(relativePath);
        Consumers = new ReadOnlyCollection<ContractConsumer>(orderedConsumers);
        ConsumerModuleIds = new ReadOnlyCollection<StructuralId>(
            orderedConsumers
                .Select(consumer => consumer.ModuleId)
                .Where(moduleId => moduleId.HasValue)
                .Select(moduleId => moduleId!.Value)
                .Distinct()
                .Order()
                .ToArray());
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>Gets the stable intelligence identifier.</summary>
    public StructuralId Id { get; }

    /// <summary>Gets the structural symbol that acts as the boundary.</summary>
    public StructuralId SymbolId { get; }

    /// <summary>Gets the contract display name.</summary>
    public string Name { get; }

    /// <summary>Gets the compact, factual summary of the observed boundary.</summary>
    public string Summary { get; }

    /// <summary>Gets the extensible reason the symbol behaves as a boundary.</summary>
    public string ContractKind { get; }

    /// <summary>Gets the language-neutral kind of the underlying symbol.</summary>
    public string SymbolKind { get; }

    /// <summary>Gets the observed declared visibility of the underlying symbol.</summary>
    public string Visibility { get; }

    /// <summary>Gets the optional qualified symbol name.</summary>
    public string? QualifiedName { get; }

    /// <summary>Gets the optional stable semantic signature.</summary>
    public string? Signature { get; }

    /// <summary>Gets the owning module, or <see langword="null"/> when module ownership is unknown.</summary>
    public StructuralId? DeclaringModuleId { get; }

    /// <summary>Gets the repository-relative declaration path used for navigation.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the sorted observed consumers that depend on this boundary.</summary>
    public IReadOnlyList<ContractConsumer> Consumers { get; }

    /// <summary>Gets the sorted distinct modules that contain at least one consumer.</summary>
    public IReadOnlyList<StructuralId> ConsumerModuleIds { get; }

    /// <summary>Gets the number of distinct consumers that break when this boundary changes shape.</summary>
    public int BlastRadius => Consumers.Count;

    /// <summary>Gets evidence, confidence, scope, freshness, and classification.</summary>
    public KnowledgeMetadata Metadata { get; }
}

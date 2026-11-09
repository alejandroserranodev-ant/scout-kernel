using System.Collections.ObjectModel;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Identifies one observed consumer of a contract and the relationships that bind it to that contract.
/// </summary>
/// <remarks>
/// Consumers are rolled up to their outermost declaring type so that blast radius is expressed in units a
/// reviewer can act on rather than in individual member references.
/// </remarks>
public sealed class ContractConsumer
{
    /// <summary>
    /// Initializes one observed contract consumer.
    /// </summary>
    /// <param name="symbolId">The consuming symbol identifier.</param>
    /// <param name="name">The consuming symbol display name.</param>
    /// <param name="moduleId">The module that owns the consumer, or <see langword="null"/> when ownership is unknown.</param>
    /// <param name="relationshipKinds">The observed relationship kinds that bind the consumer to the contract.</param>
    public ContractConsumer(
        StructuralId symbolId,
        string name,
        StructuralId? moduleId,
        IEnumerable<string> relationshipKinds)
    {
        if (symbolId.IsEmpty)
        {
            throw new ArgumentException("A contract consumer cannot use an empty symbol identifier.", nameof(symbolId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(relationshipKinds);

        var kinds = relationshipKinds
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Select(kind => kind.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (kinds.Length == 0)
        {
            throw new ArgumentException(
                "A contract consumer requires at least one observed relationship kind.",
                nameof(relationshipKinds));
        }

        SymbolId = symbolId;
        Name = name.Trim();
        ModuleId = moduleId is { IsEmpty: false } ? moduleId : null;
        RelationshipKinds = new ReadOnlyCollection<string>(kinds);
    }

    /// <summary>Gets the consuming symbol identifier.</summary>
    public StructuralId SymbolId { get; }

    /// <summary>Gets the consuming symbol display name.</summary>
    public string Name { get; }

    /// <summary>Gets the owning module, or <see langword="null"/> when module ownership is unknown.</summary>
    public StructuralId? ModuleId { get; }

    /// <summary>Gets the sorted relationship kinds that bind this consumer to the contract.</summary>
    public IReadOnlyList<string> RelationshipKinds { get; }
}

namespace Scout.Core.Ir;

/// <summary>
/// Provides the common identity, classification, name, and evidence of a structural graph entity.
/// </summary>
public abstract class StructuralEntity
{
    /// <summary>
    /// Initializes common structural entity data.
    /// </summary>
    /// <param name="id">The stable structural identifier.</param>
    /// <param name="entityKind">The extensible entity kind.</param>
    /// <param name="name">The display name.</param>
    /// <param name="evidenceIds">The evidence identifiers supporting this fact.</param>
    protected StructuralEntity(
        StructuralId id,
        string entityKind,
        string name,
        IEnumerable<StructuralId>? evidenceIds)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A structural entity cannot use an empty identifier.", nameof(id));
        }

        Id = id;
        EntityKind = IrGuard.NormalizeKind(entityKind, nameof(entityKind));
        Name = IrGuard.Required(name, nameof(name));
        EvidenceIds = IrGuard.CopyOrdered(evidenceIds, Comparer<StructuralId>.Default, distinct: true);
    }

    /// <summary>
    /// Gets the stable structural identifier.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the extensible structural entity kind.
    /// </summary>
    public string EntityKind { get; }

    /// <summary>
    /// Gets the entity display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the deterministically ordered evidence identifiers supporting the entity fact.
    /// </summary>
    public IReadOnlyList<StructuralId> EvidenceIds { get; }
}

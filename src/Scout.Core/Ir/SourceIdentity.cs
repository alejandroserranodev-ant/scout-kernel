namespace Scout.Core.Ir;

/// <summary>
/// Identifies the repository state from which a structural snapshot was compiled.
/// </summary>
public sealed class SourceIdentity
{
    /// <summary>
    /// Initializes source repository identity metadata.
    /// </summary>
    /// <param name="id">The stable repository identity.</param>
    /// <param name="name">The repository display name.</param>
    /// <param name="revisionKind">The extensible revision kind.</param>
    /// <param name="revision">The revision value, or <see langword="null"/> when unknown.</param>
    /// <param name="contentFingerprint">An optional repository content fingerprint.</param>
    public SourceIdentity(
        StructuralId id,
        string name,
        string revisionKind,
        string? revision = null,
        string? contentFingerprint = null)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("The source identity cannot use an empty identifier.", nameof(id));
        }

        Id = id;
        Name = IrGuard.Required(name, nameof(name));
        RevisionKind = IrGuard.NormalizeKind(revisionKind, nameof(revisionKind));
        Revision = string.IsNullOrWhiteSpace(revision) ? null : revision.Trim();
        ContentFingerprint = string.IsNullOrWhiteSpace(contentFingerprint) ? null : contentFingerprint.Trim();
    }

    /// <summary>
    /// Gets the stable repository identity.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the repository display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the extensible kind of source revision.
    /// </summary>
    public string RevisionKind { get; }

    /// <summary>
    /// Gets the source revision value, or <see langword="null"/> when it is unknown.
    /// </summary>
    public string? Revision { get; }

    /// <summary>
    /// Gets the optional fingerprint of the repository content included in the snapshot.
    /// </summary>
    public string? ContentFingerprint { get; }
}

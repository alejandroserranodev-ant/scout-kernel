namespace Scout.Core.Ir;

/// <summary>
/// Describes an analyzer-observed repository source file.
/// </summary>
public sealed class SourceFileFact : StructuralEntity
{
    /// <summary>
    /// Initializes a source file fact.
    /// </summary>
    /// <param name="id">The stable source file identifier.</param>
    /// <param name="relativePath">The repository-relative source path.</param>
    /// <param name="languageId">The extensible language identifier.</param>
    /// <param name="contentHash">The optional content hash.</param>
    /// <param name="hashAlgorithm">The hash algorithm when <paramref name="contentHash"/> is present.</param>
    /// <param name="evidenceIds">The evidence identifiers supporting the source file fact.</param>
    public SourceFileFact(
        StructuralId id,
        string relativePath,
        string languageId,
        string? contentHash = null,
        string? hashAlgorithm = null,
        IEnumerable<StructuralId>? evidenceIds = null)
        : base(id, KnownEntityKinds.SourceFile, RepositoryPath.GetName(relativePath), evidenceIds)
    {
        RelativePath = RepositoryPath.Normalize(relativePath);
        LanguageId = IrGuard.NormalizeKind(languageId, nameof(languageId));
        ContentHash = string.IsNullOrWhiteSpace(contentHash) ? null : contentHash.Trim();
        HashAlgorithm = string.IsNullOrWhiteSpace(hashAlgorithm)
            ? null
            : IrGuard.NormalizeKind(hashAlgorithm, nameof(hashAlgorithm));

        if ((ContentHash is null) != (HashAlgorithm is null))
        {
            throw new ArgumentException("Content hash and hash algorithm must either both be provided or both be omitted.");
        }
    }

    /// <summary>
    /// Gets the normalized repository-relative source path.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the extensible language identifier.
    /// </summary>
    public string LanguageId { get; }

    /// <summary>
    /// Gets the optional content hash.
    /// </summary>
    public string? ContentHash { get; }

    /// <summary>
    /// Gets the hash algorithm when a content hash is present.
    /// </summary>
    public string? HashAlgorithm { get; }
}

namespace Scout.Core.Ir;

/// <summary>
/// Describes an analyzer-observed source project without assigning an inferred architectural role.
/// </summary>
public sealed class ProjectFact : StructuralEntity
{
    /// <summary>
    /// Initializes a source project fact.
    /// </summary>
    /// <param name="id">The stable project identifier.</param>
    /// <param name="name">The project display name.</param>
    /// <param name="languageId">The extensible primary language identifier.</param>
    /// <param name="relativePath">The repository-relative project metadata path.</param>
    /// <param name="evidenceIds">The evidence identifiers supporting the project fact.</param>
    public ProjectFact(
        StructuralId id,
        string name,
        string languageId,
        string relativePath,
        IEnumerable<StructuralId>? evidenceIds = null)
        : base(id, KnownEntityKinds.Project, name, evidenceIds)
    {
        LanguageId = IrGuard.NormalizeKind(languageId, nameof(languageId));
        RelativePath = RepositoryPath.Normalize(relativePath);
    }

    /// <summary>
    /// Gets the extensible primary language identifier.
    /// </summary>
    public string LanguageId { get; }

    /// <summary>
    /// Gets the normalized repository-relative project metadata path.
    /// </summary>
    public string RelativePath { get; }
}

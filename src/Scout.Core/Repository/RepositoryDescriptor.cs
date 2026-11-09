namespace Scout.Core.Repository;

/// <summary>
/// Contains deterministic repository discovery results used by language analyzers.
/// </summary>
/// <param name="RootPath">The absolute repository root used only for source access.</param>
/// <param name="Name">The repository directory name.</param>
/// <param name="Files">The sorted, repository-relative input file inventory.</param>
/// <param name="ContentFingerprint">A root-independent SHA-256 fingerprint of paths and contents.</param>
/// <param name="Revision">The source-control revision, if available at this exact root.</param>
/// <param name="Diagnostics">Non-fatal discovery diagnostics.</param>
public sealed record RepositoryDescriptor(
    string RootPath,
    string Name,
    IReadOnlyList<DiscoveredFile> Files,
    string ContentFingerprint,
    SourceRevision Revision,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>
    /// Returns discovered files with the requested extension in deterministic order.
    /// </summary>
    /// <param name="extension">An extension with or without a leading dot.</param>
    /// <returns>The matching files.</returns>
    public IReadOnlyList<DiscoveredFile> FindByExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var normalized = extension.StartsWith('.') ? extension : $".{extension}";

        return Files
            .Where(file => string.Equals(
                Path.GetExtension(file.RelativePath),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}

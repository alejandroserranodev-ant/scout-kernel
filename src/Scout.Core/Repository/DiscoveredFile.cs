namespace Scout.Core.Repository;

/// <summary>
/// Describes an input file discovered below a repository root.
/// </summary>
/// <param name="RelativePath">The repository-relative path using forward slashes.</param>
/// <param name="Length">The file length in bytes.</param>
/// <param name="ContentHash">The lowercase SHA-256 content hash.</param>
public sealed record DiscoveredFile(string RelativePath, long Length, string ContentHash);

namespace Scout.Core.Repository;

/// <summary>
/// Provides portable repository path operations shared by analyzers and generators.
/// </summary>
public static class RepositoryPath
{
    /// <summary>
    /// Converts a path below a repository root into a forward-slash relative path.
    /// </summary>
    /// <param name="rootPath">The absolute repository root.</param>
    /// <param name="path">The path to normalize.</param>
    /// <returns>A portable relative path.</returns>
    /// <exception cref="ArgumentException">The path is outside the repository root.</exception>
    public static string GetRelativePath(string rootPath, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullRoot = Path.GetFullPath(rootPath);
        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(fullRoot, fullPath);

        if (relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("The path must be located below the repository root.", nameof(path));
        }

        return Normalize(relativePath);
    }

    /// <summary>
    /// Normalizes separators and removes a leading current-directory segment.
    /// </summary>
    /// <param name="relativePath">A relative path.</param>
    /// <returns>The normalized relative path.</returns>
    public static string Normalize(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalized = relativePath.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(normalized))
        {
            throw new ArgumentException("A repository path must be relative and cannot escape its root.", nameof(relativePath));
        }

        return normalized;
    }

    /// <summary>
    /// Resolves a portable relative path below an absolute repository root.
    /// </summary>
    /// <param name="rootPath">The absolute repository root.</param>
    /// <param name="relativePath">The portable relative path.</param>
    /// <returns>The validated absolute path.</returns>
    public static string Resolve(string rootPath, string relativePath)
    {
        var normalized = Normalize(relativePath);
        var candidate = Path.GetFullPath(Path.Combine(rootPath, normalized.Replace('/', Path.DirectorySeparatorChar)));
        _ = GetRelativePath(rootPath, candidate);
        return candidate;
    }
}

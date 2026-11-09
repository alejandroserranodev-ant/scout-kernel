namespace Scout.Core.Intelligence;

/// <summary>
/// Converts repository-relative paths into the portable glob scopes used by intelligence claims.
/// </summary>
internal static class RepositoryScope
{
    internal const string EntireRepository = "**";

    internal static string FromRelativePath(string relativePath)
    {
        var directory = DirectoryOf(relativePath);
        return directory == "." ? EntireRepository : $"{directory}/**";
    }

    internal static string DirectoryOf(string relativePath)
    {
        var normalized = Ir.RepositoryPath.Normalize(relativePath);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? "." : normalized[..separator];
    }
}

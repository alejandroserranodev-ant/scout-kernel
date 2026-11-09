using System.Text;

namespace Scout.Core.Ir;

/// <summary>
/// Normalizes repository-relative paths for portable identities and persisted facts.
/// </summary>
public static class RepositoryPath
{
    /// <summary>
    /// Normalizes a repository-relative path to Unicode Form C with forward slashes.
    /// </summary>
    /// <param name="path">The repository-relative path.</param>
    /// <returns>The normalized path, or <c>.</c> for the repository root.</returns>
    /// <exception cref="ArgumentException">Thrown when the path is absolute or escapes the repository root.</exception>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A repository-relative path cannot be empty.", nameof(path));
        }

        var normalized = path.Trim().Normalize(NormalizationForm.FormC).Replace('\\', '/');
        if (normalized[0] == '/' ||
            (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':'))
        {
            throw new ArgumentException("A repository path must be relative.", nameof(path));
        }

        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new ArgumentException("A repository path cannot escape the repository root.", nameof(path));
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return segments.Count == 0 ? "." : string.Join('/', segments);
    }

    /// <summary>
    /// Gets the final name component of a normalized repository-relative path.
    /// </summary>
    /// <param name="path">The repository-relative path.</param>
    /// <returns>The final path component.</returns>
    public static string GetName(string path)
    {
        var normalized = Normalize(path);
        var separator = normalized.LastIndexOf('/');
        return separator < 0 ? normalized : normalized[(separator + 1)..];
    }
}

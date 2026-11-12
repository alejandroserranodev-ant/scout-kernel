namespace Scout.Core.Retrieval;

/// <summary>
/// Picks, for each source file, the few words that most distinguish it from the rest of the repository — the
/// words a task description is likely to use — from everything the file says, comments and wording included.
/// </summary>
/// <remarks>
/// Declared names alone miss a file whose behaviour is described in prose ("serve interactive API documentation")
/// but whose symbols are named differently. Terms are scored by how often the file uses them against how many
/// files use them, so boilerplate common to most files never makes the cut. The scoring needs no word lists, so
/// it works for any language a repository is written in. The result depends only on the file contents.
/// </remarks>
public static class FileVocabulary
{
    /// <summary>The most terms kept for one file.</summary>
    public const int MaxTermsPerFile = 40;

    /// <summary>Shorter terms are too ambiguous to help a query.</summary>
    public const int MinimumTermLength = 3;

    /// <summary>Files larger than this are skipped: they are generated or data, not prose about behaviour.</summary>
    public const int MaxFileCharacters = 400_000;

    /// <summary>Builds the vocabulary for every file in <paramref name="contentsByPath"/>.</summary>
    public static IReadOnlyDictionary<string, string[]> Build(IReadOnlyDictionary<string, string> contentsByPath)
    {
        ArgumentNullException.ThrowIfNull(contentsByPath);

        var countsByPath = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (path, content) in contentsByPath)
        {
            if (content.Length > MaxFileCharacters)
            {
                continue;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var term in IdentifierTokenizer.Tokenize(content))
            {
                if (term.Length >= MinimumTermLength)
                {
                    counts[term] = counts.GetValueOrDefault(term) + 1;
                }
            }

            countsByPath[path] = counts;
            foreach (var term in counts.Keys)
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        var documentCount = countsByPath.Count;
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (path, counts) in countsByPath)
        {
            result[path] = counts
                .Select(entry => (Term: entry.Key, Score: (1 + Math.Log(entry.Value)) * Math.Log((double)documentCount / documentFrequency[entry.Key])))
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score)
                .ThenBy(entry => entry.Term, StringComparer.Ordinal)
                .Take(MaxTermsPerFile)
                .Select(entry => entry.Term)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        return result;
    }
}

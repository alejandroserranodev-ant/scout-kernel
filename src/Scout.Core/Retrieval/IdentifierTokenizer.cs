using Scout.Core.Text;

namespace Scout.Core.Retrieval;

/// <summary>
/// Splits identifiers, paths, and prose into the lowercase terms used for lexical retrieval.
/// </summary>
/// <remarks>
/// Code carries most of its meaning in identifiers, so a tokenizer that understands identifier casing
/// recovers more signal than one that only splits on whitespace. Both the whole identifier and its parts
/// are emitted, because a query may name either.
/// </remarks>
public static class IdentifierTokenizer
{
    private const int MinimumTermLength = 2;

    /// <summary>
    /// Tokenizes text into lowercase retrieval terms.
    /// </summary>
    /// <param name="text">The text to tokenize. May be <see langword="null"/> or empty.</param>
    /// <returns>The emitted terms, including whole identifiers and their case-derived parts.</returns>
    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var terms = new List<string>();
        foreach (var run in SplitRuns(DiacriticFolding.Fold(text)))
        {
            var parts = SplitCasing(run);

            // A single-part run is already its own whole form; emitting it twice would double its weight.
            if (parts.Count > 1)
            {
                AddTerm(terms, run);
            }

            foreach (var part in parts)
            {
                AddTerm(terms, part);
            }
        }

        return terms;
    }

    private static List<string> SplitRuns(string text)
    {
        var runs = new List<string>();
        var start = -1;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsAsciiLetterOrDigit(text[index]))
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                runs.Add(text[start..index]);
                start = -1;
            }
        }

        if (start >= 0)
        {
            runs.Add(text[start..]);
        }

        return runs;
    }

    // Boundaries are taken where the author of the identifier put them: a lower-to-upper transition, the
    // last capital of an acronym that begins a word, and any letter-to-digit transition.
    private static List<string> SplitCasing(string run)
    {
        var parts = new List<string>();
        var start = 0;
        for (var index = 1; index < run.Length; index++)
        {
            var previous = run[index - 1];
            var current = run[index];
            var isBoundary =
                (char.IsLower(previous) && char.IsUpper(current)) ||
                (char.IsUpper(previous) && char.IsUpper(current) && index + 1 < run.Length && char.IsLower(run[index + 1])) ||
                (char.IsAsciiLetter(previous) && char.IsAsciiDigit(current)) ||
                (char.IsAsciiDigit(previous) && char.IsAsciiLetter(current));
            if (!isBoundary)
            {
                continue;
            }

            parts.Add(run[start..index]);
            start = index;
        }

        parts.Add(run[start..]);
        return parts;
    }

    private static void AddTerm(List<string> terms, string candidate)
    {
        if (candidate.Length < MinimumTermLength)
        {
            return;
        }

        terms.Add(candidate.ToLowerInvariant());
    }
}

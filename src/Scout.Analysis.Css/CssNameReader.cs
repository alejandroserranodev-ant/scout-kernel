using System.Collections.ObjectModel;

namespace Scout.Analysis.Css;

/// <summary>
/// Extracts the declared and consumed names a style rule contains.
/// </summary>
internal static class CssNameReader
{
    /// <summary>
    /// Reads the class names a selector applies to.
    /// </summary>
    /// <param name="selector">The selector text.</param>
    /// <returns>The distinct class names in source order.</returns>
    public static IReadOnlyList<string> ReadClassNames(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inAttributeSelector = false;

        for (var index = 0; index < selector.Length; index++)
        {
            var current = selector[index];
            if (current == '[')
            {
                inAttributeSelector = true;
                continue;
            }

            if (current == ']')
            {
                inAttributeSelector = false;
                continue;
            }

            if (inAttributeSelector || current != '.')
            {
                continue;
            }

            var name = ReadIdentifier(selector, index + 1);
            if (name.Length == 0 || char.IsAsciiDigit(name[0]))
            {
                continue;
            }

            index += name.Length;
            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return new ReadOnlyCollection<string>(names);
    }

    /// <summary>
    /// Reads the custom properties a declaration block defines.
    /// </summary>
    /// <param name="body">The declaration block text.</param>
    /// <returns>The distinct custom property names in source order.</returns>
    public static IReadOnlyList<string> ReadDeclaredCustomProperties(string body)
    {
        return ReadCustomProperties(body, requireAssignment: true);
    }

    /// <summary>
    /// Reads the custom properties a declaration block consumes through <c>var()</c>.
    /// </summary>
    /// <param name="body">The declaration block text.</param>
    /// <returns>The distinct custom property names in source order.</returns>
    public static IReadOnlyList<string> ReadConsumedCustomProperties(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        while (index < body.Length)
        {
            var start = body.IndexOf("var(", index, StringComparison.Ordinal);
            if (start < 0)
            {
                break;
            }

            var nameStart = start + "var(".Length;
            while (nameStart < body.Length && char.IsWhiteSpace(body[nameStart]))
            {
                nameStart++;
            }

            index = nameStart;
            if (!StartsWithCustomPropertyPrefix(body, nameStart))
            {
                continue;
            }

            var name = ReadIdentifier(body, nameStart + 2);
            if (name.Length > 0 && seen.Add(name))
            {
                names.Add(name);
            }

            index = nameStart + 2 + name.Length;
        }

        return new ReadOnlyCollection<string>(names);
    }

    private static ReadOnlyCollection<string> ReadCustomProperties(string body, bool requireAssignment)
    {
        ArgumentNullException.ThrowIfNull(body);

        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < body.Length; index++)
        {
            if (!StartsWithCustomPropertyPrefix(body, index) || !IsDeclarationStart(body, index))
            {
                continue;
            }

            var name = ReadIdentifier(body, index + 2);
            if (name.Length == 0)
            {
                continue;
            }

            var afterName = index + 2 + name.Length;
            index = afterName - 1;

            if (requireAssignment && !IsFollowedByAssignment(body, afterName))
            {
                continue;
            }

            if (seen.Add(name))
            {
                names.Add(name);
            }
        }

        return new ReadOnlyCollection<string>(names);
    }

    private static bool StartsWithCustomPropertyPrefix(string text, int index)
    {
        return index + 1 < text.Length && text[index] == '-' && text[index + 1] == '-';
    }

    private static bool IsDeclarationStart(string text, int index)
    {
        for (var probe = index - 1; probe >= 0; probe--)
        {
            var current = text[probe];
            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            return current is ';' or '{';
        }

        return true;
    }

    private static bool IsFollowedByAssignment(string text, int index)
    {
        for (var probe = index; probe < text.Length; probe++)
        {
            var current = text[probe];
            if (char.IsWhiteSpace(current))
            {
                continue;
            }

            return current == ':';
        }

        return false;
    }

    private static string ReadIdentifier(string text, int start)
    {
        var end = start;
        while (end < text.Length && IsIdentifierCharacter(text[end]))
        {
            end++;
        }

        return end == start ? string.Empty : text[start..end];
    }

    private static bool IsIdentifierCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '-' or '_' || value > 0x7F;
    }
}

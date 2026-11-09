using System.Collections.ObjectModel;
using System.Text;

namespace Scout.Analysis.Css;

/// <summary>
/// Reads a style sheet and reports only what the text literally declares.
/// </summary>
/// <remarks>
/// <para>
/// The reader is a scanner rather than a full CSS parser. It resolves the two things an agent needs in order
/// to change styles safely: which names a file declares, and which custom properties a rule consumes. It never
/// resolves the cascade, specificity or computed values, because those depend on document structure that no
/// style sheet contains.
/// </para>
/// <para>
/// Constructs the scanner does not understand are skipped rather than approximated, so an unusual style sheet
/// yields fewer declarations instead of wrong ones.
/// </para>
/// </remarks>
internal static class CssStyleSheetReader
{
    private const char BlockStart = '{';
    private const char BlockEnd = '}';

    /// <summary>
    /// Reads every rule a style sheet declares.
    /// </summary>
    /// <param name="content">The style sheet text.</param>
    /// <returns>The declared rules in source order.</returns>
    public static IReadOnlyList<CssRule> Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rules = new List<CssRule>();
        var cursor = new Cursor(content);
        ReadRules(cursor, rules, depth: 0);
        return new ReadOnlyCollection<CssRule>(rules);
    }

    private static void ReadRules(Cursor cursor, List<CssRule> rules, int depth)
    {
        var prelude = new StringBuilder();
        var preludeStart = cursor.Position;

        while (!cursor.IsAtEnd)
        {
            if (cursor.TrySkipTrivia())
            {
                continue;
            }

            var current = cursor.Current;
            if (current == BlockEnd)
            {
                cursor.Advance();
                return;
            }

            if (current == ';')
            {
                cursor.Advance();
                prelude.Clear();
                preludeStart = cursor.Position;
                continue;
            }

            if (current == BlockStart)
            {
                var selectorText = prelude.ToString().Trim();
                cursor.Advance();

                if (IsNestingAtRule(selectorText))
                {
                    ReadRules(cursor, rules, depth + 1);
                }
                else
                {
                    var body = ReadBlockBody(cursor);
                    if (selectorText.Length > 0 && !selectorText.StartsWith('@'))
                    {
                        rules.Add(new CssRule(selectorText, preludeStart, body));
                    }
                }

                prelude.Clear();
                preludeStart = cursor.Position;
                continue;
            }

            if (prelude.Length == 0)
            {
                preludeStart = cursor.Position;
            }

            prelude.Append(current);
            cursor.Advance();
        }
    }

    private static string ReadBlockBody(Cursor cursor)
    {
        var body = new StringBuilder();
        var nesting = 0;

        while (!cursor.IsAtEnd)
        {
            if (cursor.TrySkipTrivia(body))
            {
                continue;
            }

            var current = cursor.Current;
            if (current == BlockStart)
            {
                nesting++;
            }
            else if (current == BlockEnd)
            {
                if (nesting == 0)
                {
                    cursor.Advance();
                    return body.ToString();
                }

                nesting--;
            }

            body.Append(current);
            cursor.Advance();
        }

        return body.ToString();
    }

    private static bool IsNestingAtRule(string prelude)
    {
        if (!prelude.StartsWith('@'))
        {
            return false;
        }

        var name = prelude[1..];
        var end = name.IndexOfAny([' ', '\t', '\r', '\n', '(']);
        if (end >= 0)
        {
            name = name[..end];
        }

        return name.Equals("media", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("supports", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("layer", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("container", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("scope", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Cursor(string content)
    {
        public int Position { get; private set; }

        public bool IsAtEnd => Position >= content.Length;

        public char Current => content[Position];

        public void Advance() => Position++;

        /// <summary>
        /// Consumes a comment or a quoted string so their contents are never mistaken for syntax.
        /// </summary>
        /// <param name="preserved">An optional buffer that receives consumed string literals.</param>
        /// <returns><see langword="true"/> when trivia was consumed.</returns>
        public bool TrySkipTrivia(StringBuilder? preserved = null)
        {
            if (IsAtEnd)
            {
                return false;
            }

            if (Current == '/' && Position + 1 < content.Length && content[Position + 1] == '*')
            {
                var end = content.IndexOf("*/", Position + 2, StringComparison.Ordinal);
                Position = end < 0 ? content.Length : end + 2;
                return true;
            }

            if (Current is not ('"' or '\''))
            {
                return false;
            }

            var quote = Current;
            var start = Position;
            Advance();
            while (!IsAtEnd && Current != quote)
            {
                if (Current == '\\' && Position + 1 < content.Length)
                {
                    Advance();
                }

                Advance();
            }

            if (!IsAtEnd)
            {
                Advance();
            }

            preserved?.Append(content, start, Position - start);
            return true;
        }
    }
}

/// <summary>
/// Describes one declared style rule.
/// </summary>
/// <param name="Selector">The selector text exactly as written.</param>
/// <param name="SelectorOffset">The zero-based character offset of the selector.</param>
/// <param name="Body">The declaration block text with comments removed.</param>
internal sealed record CssRule(string Selector, int SelectorOffset, string Body);

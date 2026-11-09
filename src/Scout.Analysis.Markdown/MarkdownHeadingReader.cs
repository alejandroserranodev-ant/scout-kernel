namespace Scout.Analysis.Markdown;

/// <summary>
/// Reads the ATX headings of a Markdown document.
/// </summary>
/// <remarks>
/// <para>
/// Only headings are read: they are the one structure every Markdown dialect agrees on, and the one a reader
/// navigates by. Prose, links and inline markup are never interpreted beyond what a heading title needs to be
/// a readable name.
/// </para>
/// <para>
/// Text that merely looks like a heading is skipped when it sits in YAML front matter, a fenced code block or
/// an HTML comment. Setext headings (underlined with <c>===</c> or <c>---</c>) are not read, because telling
/// them apart from a paragraph followed by a thematic break depends on block structure this reader does not
/// build.
/// </para>
/// </remarks>
internal static class MarkdownHeadingReader
{
    private const int MaximumLevel = 6;
    private const int MaximumIndent = 3;

    public static IReadOnlyList<MarkdownHeading> Read(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var headings = new List<MarkdownHeading>();
        var offset = 0;
        var firstLine = true;
        var inFrontMatter = false;
        var inComment = false;
        char fenceCharacter = default;
        var fenceLength = 0;

        while (offset < content.Length)
        {
            var lineEnd = content.IndexOf('\n', offset);
            var nextOffset = lineEnd < 0 ? content.Length : lineEnd + 1;
            var line = content.AsSpan(offset, (lineEnd < 0 ? content.Length : lineEnd) - offset).TrimEnd('\r');

            if (firstLine && IsFrontMatterDelimiter(line, opening: true) && HasClosingFrontMatter(content, nextOffset))
            {
                inFrontMatter = true;
            }
            else if (inFrontMatter)
            {
                inFrontMatter = !IsFrontMatterDelimiter(line, opening: false);
            }
            else if (fenceLength > 0)
            {
                if (ClosesFence(line, fenceCharacter, fenceLength))
                {
                    fenceLength = 0;
                }
            }
            else if (inComment)
            {
                inComment = !line.Contains("-->", StringComparison.Ordinal);
            }
            else if (TryOpenFence(line, out fenceCharacter, out fenceLength))
            {
                // The fence line itself is never a heading; the loop continues inside the block.
            }
            else if (OpensComment(line))
            {
                inComment = true;
            }
            else if (TryReadHeading(line, offset, out var heading))
            {
                headings.Add(heading);
            }

            firstLine = false;
            offset = nextOffset;
        }

        return headings;
    }

    private static bool IsFrontMatterDelimiter(ReadOnlySpan<char> line, bool opening)
    {
        var trimmed = line.TrimEnd();
        return trimmed.SequenceEqual("---") || (!opening && trimmed.SequenceEqual("..."));
    }

    private static bool HasClosingFrontMatter(string content, int from)
    {
        var offset = from;
        while (offset < content.Length)
        {
            var lineEnd = content.IndexOf('\n', offset);
            var end = lineEnd < 0 ? content.Length : lineEnd;
            if (IsFrontMatterDelimiter(content.AsSpan(offset, end - offset).TrimEnd('\r'), opening: false))
            {
                return true;
            }

            offset = end + 1;
        }

        // An opening delimiter that is never closed is a thematic break, not front matter.
        return false;
    }

    private static bool TryOpenFence(ReadOnlySpan<char> line, out char character, out int length)
    {
        character = default;
        length = 0;

        var body = SkipIndent(line);
        if (body.IsEmpty || (body[0] != '`' && body[0] != '~'))
        {
            return false;
        }

        var run = CountLeading(body, body[0]);
        if (run < 3)
        {
            return false;
        }

        // A backtick fence cannot carry a backtick in its info string; that line is inline code instead.
        if (body[0] == '`' && body[run..].Contains('`'))
        {
            return false;
        }

        character = body[0];
        length = run;
        return true;
    }

    private static bool ClosesFence(ReadOnlySpan<char> line, char character, int length)
    {
        var body = SkipIndent(line);
        if (body.IsEmpty || body[0] != character)
        {
            return false;
        }

        var run = CountLeading(body, character);
        return run >= length && body[run..].Trim().IsEmpty;
    }

    private static bool OpensComment(ReadOnlySpan<char> line)
    {
        var start = line.IndexOf("<!--", StringComparison.Ordinal);
        return start >= 0 && !line[(start + 4)..].Contains("-->", StringComparison.Ordinal);
    }

    private static bool TryReadHeading(ReadOnlySpan<char> line, int lineOffset, out MarkdownHeading heading)
    {
        heading = default;

        var body = SkipIndent(line);
        var indent = line.Length - body.Length;
        var level = CountLeading(body, '#');
        if (level is < 1 or > MaximumLevel)
        {
            return false;
        }

        var rest = body[level..];
        if (!rest.IsEmpty && rest[0] != ' ' && rest[0] != '\t')
        {
            return false;
        }

        var title = MarkdownTitle.Clean(StripClosingSequence(rest.Trim()));
        if (title.Length == 0)
        {
            return false;
        }

        heading = new MarkdownHeading(level, title, lineOffset + indent, line.TrimEnd().Length - indent);
        return true;
    }

    // "## Title ##" closes with a run of '#' preceded by whitespace; "## C#" does not.
    private static ReadOnlySpan<char> StripClosingSequence(ReadOnlySpan<char> text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] == '#')
        {
            end--;
        }

        if (end == text.Length)
        {
            return text;
        }

        if (end == 0)
        {
            return [];
        }

        return text[end - 1] is ' ' or '\t' ? text[..end].TrimEnd() : text;
    }

    private static ReadOnlySpan<char> SkipIndent(ReadOnlySpan<char> line)
    {
        var indent = 0;
        while (indent < line.Length && indent <= MaximumIndent && line[indent] == ' ')
        {
            indent++;
        }

        // Four or more spaces make an indented code block, which cannot hold a heading or a fence.
        return indent > MaximumIndent ? [] : line[indent..];
    }

    private static int CountLeading(ReadOnlySpan<char> text, char character)
    {
        var count = 0;
        while (count < text.Length && text[count] == character)
        {
            count++;
        }

        return count;
    }
}

/// <summary>
/// A heading found in a Markdown document.
/// </summary>
/// <param name="Level">The heading level, from 1 to 6.</param>
/// <param name="Title">The readable heading title.</param>
/// <param name="Offset">The zero-based character offset of the heading marker.</param>
/// <param name="Length">The number of characters the heading line spans.</param>
internal readonly record struct MarkdownHeading(int Level, string Title, int Offset, int Length);

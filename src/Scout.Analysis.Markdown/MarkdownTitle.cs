using System.Text;

namespace Scout.Analysis.Markdown;

/// <summary>
/// Turns the raw text of a heading into the readable name a reader would give it.
/// </summary>
/// <remarks>
/// Inline markup carries no meaning for a name, so links and images keep only their label and emphasis and
/// code markers are dropped. Underscores are kept: they are far more often part of an identifier than emphasis.
/// </remarks>
internal static class MarkdownTitle
{
    public static string Clean(ReadOnlySpan<char> raw)
    {
        var builder = new StringBuilder(raw.Length);
        var index = 0;
        while (index < raw.Length)
        {
            var character = raw[index];
            if (character == '\\' && index + 1 < raw.Length)
            {
                builder.Append(raw[index + 1]);
                index += 2;
                continue;
            }

            if (character is '`' or '*')
            {
                index++;
                continue;
            }

            if (character == '!' && index + 1 < raw.Length && raw[index + 1] == '[')
            {
                index++;
                continue;
            }

            if (character == '[' && TryReadLinkLabel(raw, index, out var label, out var consumed))
            {
                builder.Append(Clean(label));
                index += consumed;
                continue;
            }

            builder.Append(character);
            index++;
        }

        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    // Reads "[label](target)" and "[label][reference]"; a bare "[text]" is left as written.
    private static bool TryReadLinkLabel(ReadOnlySpan<char> raw, int start, out string label, out int consumed)
    {
        label = string.Empty;
        consumed = 0;

        var close = raw[(start + 1)..].IndexOf(']');
        if (close < 0)
        {
            return false;
        }

        var afterLabel = start + 1 + close + 1;
        if (afterLabel >= raw.Length || (raw[afterLabel] != '(' && raw[afterLabel] != '['))
        {
            return false;
        }

        var closer = raw[afterLabel] == '(' ? ')' : ']';
        var end = raw[(afterLabel + 1)..].IndexOf(closer);
        if (end < 0)
        {
            return false;
        }

        label = raw.Slice(start + 1, close).ToString();
        consumed = afterLabel + 1 + end + 1 - start;
        return true;
    }
}

using Scout.Core.Ir;

namespace Scout.Analysis.Markdown;

/// <summary>
/// Converts character offsets into one-based line and column coordinates.
/// </summary>
internal sealed class LineIndex
{
    private const string PositionEncoding = "utf-16-code-units";

    private readonly int[] lineStarts;
    private readonly int length;

    /// <summary>
    /// Indexes the line starts of a source text.
    /// </summary>
    /// <param name="content">The source text.</param>
    public LineIndex(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        length = content.Length;
        var starts = new List<int> { 0 };
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] == '\n')
            {
                starts.Add(index + 1);
            }
        }

        lineStarts = [.. starts];
    }

    /// <summary>
    /// Creates a source location spanning a range of characters.
    /// </summary>
    /// <param name="relativePath">The repository-relative source path.</param>
    /// <param name="offset">The zero-based start offset.</param>
    /// <param name="span">The length of the range in characters.</param>
    /// <param name="sourceFileId">The identifier of the containing source file.</param>
    /// <returns>The one-based source location.</returns>
    public SourceLocation CreateLocation(string relativePath, int offset, int span, StructuralId sourceFileId)
    {
        var start = Math.Clamp(offset, 0, length);
        var end = Math.Clamp(offset + Math.Max(span, 1), start, length);
        var (startLine, startColumn) = Resolve(start);
        var (endLine, endColumn) = Resolve(end);
        return new SourceLocation(
            relativePath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            PositionEncoding,
            sourceFileId);
    }

    private (int Line, int Column) Resolve(int offset)
    {
        var index = Array.BinarySearch(lineStarts, offset);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return (index + 1, offset - lineStarts[index] + 1);
    }
}

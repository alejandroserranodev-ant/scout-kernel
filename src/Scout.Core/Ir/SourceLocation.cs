namespace Scout.Core.Ir;

/// <summary>
/// Locates evidence or a declaration in a repository source file.
/// </summary>
public sealed class SourceLocation
{
    internal static IComparer<SourceLocation> DeterministicComparer { get; } =
        Comparer<SourceLocation>.Create(CompareLocations);

    /// <summary>
    /// Initializes a source location using one-based line and column coordinates.
    /// </summary>
    /// <param name="relativePath">The repository-relative source path.</param>
    /// <param name="startLine">The one-based start line.</param>
    /// <param name="startColumn">The one-based start column.</param>
    /// <param name="endLine">The one-based inclusive end line.</param>
    /// <param name="endColumn">The one-based exclusive end column.</param>
    /// <param name="positionEncoding">The extensible column encoding identifier.</param>
    /// <param name="sourceFileId">The optional structural identifier of the corresponding source file.</param>
    public SourceLocation(
        string relativePath,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        string positionEncoding,
        StructuralId? sourceFileId = null)
    {
        if (startLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startLine), "Source lines are one-based.");
        }

        if (startColumn < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(startColumn), "Source columns are one-based.");
        }

        if (endLine < startLine)
        {
            throw new ArgumentOutOfRangeException(nameof(endLine), "The end line cannot precede the start line.");
        }

        if (endColumn < 1 || (endLine == startLine && endColumn < startColumn))
        {
            throw new ArgumentOutOfRangeException(nameof(endColumn), "The end position cannot precede the start position.");
        }

        if (sourceFileId is { IsEmpty: true })
        {
            throw new ArgumentException("A source file identifier cannot be empty.", nameof(sourceFileId));
        }

        RelativePath = RepositoryPath.Normalize(relativePath);
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        PositionEncoding = IrGuard.NormalizeKind(positionEncoding, nameof(positionEncoding));
        SourceFileId = sourceFileId;
    }

    /// <summary>
    /// Gets the normalized repository-relative source path.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the one-based start line.
    /// </summary>
    public int StartLine { get; }

    /// <summary>
    /// Gets the one-based start column.
    /// </summary>
    public int StartColumn { get; }

    /// <summary>
    /// Gets the one-based inclusive end line.
    /// </summary>
    public int EndLine { get; }

    /// <summary>
    /// Gets the one-based exclusive end column.
    /// </summary>
    public int EndColumn { get; }

    /// <summary>
    /// Gets the extensible coordinate encoding, such as <c>utf-16-code-units</c>.
    /// </summary>
    public string PositionEncoding { get; }

    /// <summary>
    /// Gets the corresponding source file identifier when known.
    /// </summary>
    public StructuralId? SourceFileId { get; }

    private static int CompareLocations(SourceLocation? left, SourceLocation? right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var comparison = StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.StartLine.CompareTo(right.StartLine);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.StartColumn.CompareTo(right.StartColumn);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.EndLine.CompareTo(right.EndLine);
        return comparison != 0 ? comparison : left.EndColumn.CompareTo(right.EndColumn);
    }
}

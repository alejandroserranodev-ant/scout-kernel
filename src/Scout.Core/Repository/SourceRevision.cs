namespace Scout.Core.Repository;

/// <summary>
/// Describes the source-control revision observed during repository discovery.
/// </summary>
/// <param name="Kind">The revision mechanism, such as <c>git</c> or <c>unknown</c>.</param>
/// <param name="Value">The revision value when one is available.</param>
/// <param name="IsDirty">Whether tracked source changes were present.</param>
public sealed record SourceRevision(string Kind, string? Value, bool IsDirty)
{
    /// <summary>
    /// Gets a revision used when no source-control metadata can be resolved.
    /// </summary>
    public static SourceRevision Unknown { get; } = new("unknown", null, false);
}

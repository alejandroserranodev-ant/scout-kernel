namespace Scout.Core.Ir;

/// <summary>
/// Records the analyzer implementation and execution state that contributed facts to a snapshot.
/// </summary>
public sealed class AnalyzerRunMetadata
{
    /// <summary>
    /// Initializes analyzer run metadata.
    /// </summary>
    /// <param name="analyzerId">The stable analyzer identifier.</param>
    /// <param name="name">The analyzer display name.</param>
    /// <param name="version">The analyzer version.</param>
    /// <param name="languageId">The extensible language identifier.</param>
    /// <param name="status">The extensible run status.</param>
    /// <param name="completedAtUtc">The completion timestamp.</param>
    /// <param name="positionEncoding">The default source position encoding emitted by the analyzer.</param>
    public AnalyzerRunMetadata(
        string analyzerId,
        string name,
        string version,
        string languageId,
        string status,
        DateTimeOffset completedAtUtc,
        string positionEncoding)
    {
        AnalyzerId = IrGuard.NormalizeKind(analyzerId, nameof(analyzerId));
        Name = IrGuard.Required(name, nameof(name));
        Version = IrGuard.Required(version, nameof(version));
        LanguageId = IrGuard.NormalizeKind(languageId, nameof(languageId));
        Status = IrGuard.NormalizeKind(status, nameof(status));
        CompletedAtUtc = completedAtUtc.ToUniversalTime();
        PositionEncoding = IrGuard.NormalizeKind(positionEncoding, nameof(positionEncoding));
    }

    /// <summary>
    /// Gets the stable analyzer identifier.
    /// </summary>
    public string AnalyzerId { get; }

    /// <summary>
    /// Gets the analyzer display name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the analyzer version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the extensible language identifier.
    /// </summary>
    public string LanguageId { get; }

    /// <summary>
    /// Gets the extensible run status.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets the UTC completion timestamp.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; }

    /// <summary>
    /// Gets the default position encoding emitted by the analyzer.
    /// </summary>
    public string PositionEncoding { get; }
}

namespace Scout.Core.Ir;

/// <summary>
/// Records an analyzer diagnostic without converting incomplete analysis into false facts.
/// </summary>
public sealed class AnalysisDiagnostic
{
    /// <summary>
    /// Initializes an analysis diagnostic.
    /// </summary>
    /// <param name="id">The stable diagnostic identifier.</param>
    /// <param name="analyzerId">The analyzer that reported the diagnostic.</param>
    /// <param name="severity">The extensible severity identifier.</param>
    /// <param name="code">The stable analyzer diagnostic code.</param>
    /// <param name="message">The diagnostic message.</param>
    /// <param name="location">The optional related source location.</param>
    public AnalysisDiagnostic(
        StructuralId id,
        string analyzerId,
        string severity,
        string code,
        string message,
        SourceLocation? location = null)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A diagnostic cannot use an empty identifier.", nameof(id));
        }

        Id = id;
        AnalyzerId = IrGuard.NormalizeKind(analyzerId, nameof(analyzerId));
        Severity = IrGuard.NormalizeKind(severity, nameof(severity));
        Code = IrGuard.Required(code, nameof(code));
        Message = IrGuard.Required(message, nameof(message));
        Location = location;
    }

    /// <summary>
    /// Gets the stable diagnostic identifier.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the identifier of the analyzer that reported the diagnostic.
    /// </summary>
    public string AnalyzerId { get; }

    /// <summary>
    /// Gets the extensible diagnostic severity.
    /// </summary>
    public string Severity { get; }

    /// <summary>
    /// Gets the stable analyzer diagnostic code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Gets the diagnostic message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional related source location.
    /// </summary>
    public SourceLocation? Location { get; }
}

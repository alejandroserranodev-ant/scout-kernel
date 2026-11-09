namespace Scout.Core.Graph;

/// <summary>
/// Represents an integrity error encountered while deriving a project graph from a structural snapshot.
/// </summary>
public sealed class ProjectGraphValidationException : Exception
{
    /// <summary>
    /// Initializes a project graph validation exception.
    /// </summary>
    /// <param name="message">The integrity error description.</param>
    public ProjectGraphValidationException(string message)
        : base(message)
    {
    }
}

namespace Scout.Core.Intelligence;

/// <summary>
/// Describes how likely an intelligence claim is to represent current project practice.
/// </summary>
public enum KnowledgeFreshness
{
    /// <summary>The evidence belongs to the compiled source snapshot.</summary>
    Current,

    /// <summary>The claim is probably current, but recency is not fully established.</summary>
    LikelyCurrent,

    /// <summary>The claim appears to describe legacy practice.</summary>
    Legacy,

    /// <summary>The evidence contains both current and legacy practice.</summary>
    Mixed,

    /// <summary>Freshness could not be established.</summary>
    Unknown
}

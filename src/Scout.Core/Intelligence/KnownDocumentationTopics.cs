namespace Scout.Core.Intelligence;

/// <summary>
/// The subjects a documentation section can be titled for, named after the intelligence areas they inform.
/// </summary>
public static class KnownDocumentationTopics
{
    /// <summary>Sections describing how the system is divided and why.</summary>
    public const string Architecture = "architecture";

    /// <summary>Sections stating how code is written or organized.</summary>
    public const string Conventions = "conventions";

    /// <summary>Sections describing interfaces other code or systems depend on.</summary>
    public const string Contracts = "contracts";

    /// <summary>Sections about cross-cutting qualities such as security, performance, risks and limits.</summary>
    public const string Concerns = "concerns";

    /// <summary>Sections describing how work or data moves through the system.</summary>
    public const string Flows = "flows";

    /// <summary>Sections recording a decision and the reasons behind it.</summary>
    public const string Decisions = "decisions";

    /// <summary>Sections about setting up, configuring, deploying or operating the system.</summary>
    public const string Operations = "operations";

    /// <summary>Sections about testing and verification.</summary>
    public const string Testing = "testing";

    /// <summary>
    /// Gets every topic in the order they are reported.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Architecture,
        Conventions,
        Contracts,
        Concerns,
        Flows,
        Decisions,
        Operations,
        Testing
    ];
}

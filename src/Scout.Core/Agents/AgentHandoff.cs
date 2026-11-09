namespace Scout.Core.Agents;

/// <summary>
/// A permitted transfer of work from one agent role to another.
/// </summary>
/// <param name="TargetRoleId">The role receiving the work.</param>
/// <param name="Mode">How control moves. See <see cref="KnownHandoffModes"/>.</param>
/// <param name="Condition">The observable condition under which the transfer is appropriate.</param>
public readonly record struct AgentHandoff(string TargetRoleId, string Mode, string Condition);

/// <summary>
/// How control moves between agent roles.
/// </summary>
public static class KnownHandoffModes
{
    /// <summary>
    /// Control passes on and does not come back. The sender's work is finished.
    /// </summary>
    public const string Sequential = "sequential";

    /// <summary>
    /// A bounded sub-session answers one question and returns control to the caller, which keeps its context.
    /// Used when a role needs another role's judgement without giving up ownership of the task.
    /// </summary>
    public const string Subsession = "subsession";
}

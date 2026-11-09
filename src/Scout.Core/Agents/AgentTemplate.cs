using System.Collections.ObjectModel;

namespace Scout.Core.Agents;

/// <summary>
/// A specialized agent role, expressed as data rather than as code bound to any orchestration framework.
/// </summary>
/// <remarks>
/// <para>
/// A template is a projection of one kernel for one role. Every role reads the same kernel, but none of them
/// reads all of it: an architect needs boundaries and module shape, a developer needs the contracts governing
/// the symbols it is about to touch. Restricting what a role loads is what keeps a multi-agent run from
/// costing as much as a single agent that reads everything.
/// </para>
/// <para>
/// Templates are emitted as data because the choice of framework belongs to whoever runs the agents. The same
/// file can drive a hand-written loop or a graph-based runtime without Scout taking a position on either.
/// </para>
/// </remarks>
public sealed class AgentTemplate
{
    /// <summary>
    /// Initializes an agent template.
    /// </summary>
    /// <param name="id">The stable role identifier.</param>
    /// <param name="role">The human-readable role name.</param>
    /// <param name="mandate">What this role is accountable for in this specific repository.</param>
    /// <param name="reads">The kernel paths this role loads, which must all exist in the kernel.</param>
    /// <param name="retrievalKinds">The card kinds this role retrieves by default.</param>
    /// <param name="retrievalLimit">The default number of cards to retrieve per query.</param>
    /// <param name="obligations">What this role must verify before handing work on.</param>
    /// <param name="handoffs">The transfers this role is permitted to make.</param>
    /// <param name="autoQueries">Queries to execute automatically before role execution (for token efficiency).</param>
    public AgentTemplate(
        string id,
        string role,
        string mandate,
        IEnumerable<string> reads,
        IEnumerable<string> retrievalKinds,
        int retrievalLimit,
        IEnumerable<string> obligations,
        IEnumerable<AgentHandoff> handoffs,
        IEnumerable<AutoQuery>? autoQueries = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(mandate);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(retrievalKinds);
        ArgumentNullException.ThrowIfNull(obligations);
        ArgumentNullException.ThrowIfNull(handoffs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retrievalLimit);

        Id = id;
        Role = role;
        Mandate = mandate;
        Reads = new ReadOnlyCollection<string>(reads.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        RetrievalKinds = new ReadOnlyCollection<string>(retrievalKinds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        RetrievalLimit = retrievalLimit;
        Obligations = new ReadOnlyCollection<string>(obligations.ToArray());
        Handoffs = new ReadOnlyCollection<AgentHandoff>(
            handoffs.OrderBy(handoff => handoff.TargetRoleId, StringComparer.Ordinal).ToArray());
        AutoQueries = new ReadOnlyCollection<AutoQuery>(autoQueries?.ToArray() ?? []);
    }

    /// <summary>Gets the stable role identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the human-readable role name.</summary>
    public string Role { get; }

    /// <summary>Gets what this role is accountable for in this specific repository.</summary>
    public string Mandate { get; }

    /// <summary>Gets the kernel paths this role loads.</summary>
    public IReadOnlyList<string> Reads { get; }

    /// <summary>Gets the card kinds this role retrieves by default.</summary>
    public IReadOnlyList<string> RetrievalKinds { get; }

    /// <summary>Gets the default number of cards to retrieve per query.</summary>
    public int RetrievalLimit { get; }

    /// <summary>Gets what this role must verify before handing work on.</summary>
    public IReadOnlyList<string> Obligations { get; }

    /// <summary>Gets the transfers this role is permitted to make.</summary>
    public IReadOnlyList<AgentHandoff> Handoffs { get; }

    /// <summary>Gets queries to execute automatically before role execution (for token efficiency).</summary>
    public IReadOnlyList<AutoQuery> AutoQueries { get; }
}

/// <summary>
/// An automatic retrieval query executed by scout-agents before role execution, optimized for token efficiency.
/// </summary>
public sealed class AutoQuery
{
    /// <summary>Initializes an automatic query.</summary>
    /// <param name="query">The search term or description of what to find.</param>
    /// <param name="kind">The card kind to filter by (optional).</param>
    /// <param name="limit">Maximum results to return.</param>
    public AutoQuery(string query, string? kind = null, int limit = 8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        Query = query;
        Kind = kind;
        Limit = limit;
    }

    /// <summary>The search term or description of what to find.</summary>
    public string Query { get; }

    /// <summary>The card kind to filter by (null = all kinds).</summary>
    public string? Kind { get; }

    /// <summary>Maximum results to return.</summary>
    public int Limit { get; }
}

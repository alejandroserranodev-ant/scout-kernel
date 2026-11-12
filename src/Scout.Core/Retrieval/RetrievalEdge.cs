namespace Scout.Core.Retrieval;

/// <summary>
/// A typed, directed relationship between two retrieval cards, taken from the relationships an analyzer observed.
/// </summary>
/// <param name="SourceId">The id of the card the relationship starts at.</param>
/// <param name="TargetId">The id of the card it points to.</param>
/// <param name="Kind">The observed relationship kind (see <see cref="Scout.Core.Ir.KnownRelationshipKinds"/>).</param>
public sealed record RetrievalEdge(string SourceId, string TargetId, string Kind);

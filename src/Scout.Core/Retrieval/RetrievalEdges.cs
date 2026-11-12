using Scout.Core.Intelligence;
using Scout.Core.Ir;

namespace Scout.Core.Retrieval;

/// <summary>Derives the edges between retrieval cards from the structural relationships behind them.</summary>
public static class RetrievalEdges
{
    // Relationships that say how one declaration relates to another. File-level "defines" and project-level
    // dependencies are left out: their endpoints are not cards, and a file is already a card of its own.
    private static readonly string[] EdgeKinds =
    [
        KnownRelationshipKinds.Calls,
        KnownRelationshipKinds.References,
        KnownRelationshipKinds.Renders,
        KnownRelationshipKinds.Inherits,
        KnownRelationshipKinds.Implements,
        KnownRelationshipKinds.Contains
    ];

    /// <summary>
    /// Builds the edges whose two ends are both cards, mapping a symbol that was promoted to a contract card onto
    /// that card. Sorted and de-duplicated, so the same repository always yields the same edges.
    /// </summary>
    public static IReadOnlyList<RetrievalEdge> Build(
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        IReadOnlyList<RetrievalCard> cards)
    {
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(intelligence);
        ArgumentNullException.ThrowIfNull(cards);

        var cardIds = cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        var cardBySymbol = new Dictionary<StructuralId, string>();
        foreach (var symbol in structural.Symbols.Where(symbol => cardIds.Contains(symbol.Id.Value)))
        {
            cardBySymbol[symbol.Id] = symbol.Id.Value;
        }

        foreach (var contract in intelligence.Contracts.Where(contract => cardIds.Contains(contract.Id.Value)))
        {
            cardBySymbol[contract.SymbolId] = contract.Id.Value;
        }

        var edges = new HashSet<RetrievalEdge>();
        foreach (var relationship in structural.Relationships.Where(r => EdgeKinds.Contains(r.Kind, StringComparer.Ordinal)))
        {
            if (cardBySymbol.TryGetValue(relationship.SourceId, out var source)
                && cardBySymbol.TryGetValue(relationship.TargetId, out var target)
                && !string.Equals(source, target, StringComparison.Ordinal))
            {
                edges.Add(new RetrievalEdge(source, target, relationship.Kind));
            }
        }

        return edges
            .OrderBy(edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(edge => edge.SourceId, StringComparer.Ordinal)
            .ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
            .ToArray();
    }
}

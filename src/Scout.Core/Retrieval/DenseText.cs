using System.Text;

namespace Scout.Core.Retrieval;

/// <summary>
/// Builds the text a dense model reads for a card.
/// </summary>
/// <remarks>
/// A dense model reads prose, not a bag of terms. The text keeps the card's name, its summary and its neighbour
/// names, and spells an identifier out in words ("waLink" also reads "wa link"), because that is the form in which
/// a question mentions it. The length is bounded so a card with a long neighbour list cannot dominate the cost.
/// </remarks>
public static class DenseText
{
    private const int MaxLength = 500;

    /// <summary>
    /// Builds the text for a card.
    /// </summary>
    /// <param name="card">The card to describe.</param>
    /// <returns>The text to embed.</returns>
    public static string Build(RetrievalCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        var builder = new StringBuilder();
        builder.Append(card.Kind).Append(": ").Append(card.Name);
        if (!card.Name.Contains(' ', StringComparison.Ordinal))
        {
            builder.Append(" (").AppendJoin(' ', IdentifierTokenizer.Tokenize(card.Name)).Append(')');
        }

        builder.Append(". ").Append(card.Summary);
        if (card.Related.Count > 0)
        {
            builder.Append(" Related: ").AppendJoin(", ", card.Related);
        }

        return builder.Length <= MaxLength ? builder.ToString() : builder.ToString(0, MaxLength);
    }
}

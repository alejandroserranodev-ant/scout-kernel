using System.Collections.ObjectModel;

namespace Scout.Core.Retrieval;

/// <summary>
/// The default vector space: term frequency weighted by inverse document frequency over identifier subwords.
/// </summary>
/// <remarks>
/// <para>
/// This model is deterministic, needs no network, no key and no model download, and therefore keeps a Scout
/// Kernel reproducible and self-contained. It is honest about its limit: it matches wording, not meaning.
/// A query for wording the repository never uses will not match, however related the concepts are.
/// </para>
/// <para>
/// That limit costs less here than it would over prose, because a retrieval corpus built from identifiers,
/// paths and generated summaries shares its vocabulary with the questions asked about it. Inverse document
/// frequency also removes the need for a stopword list: a term every card contains carries no weight on its
/// own, so structural noise is demoted by evidence instead of by a hand-written list.
/// </para>
/// </remarks>
public sealed class LexicalEmbeddingModel : IEmbeddingModel
{
    /// <inheritdoc />
    public string ModelId => "scout-lexical-tfidf-1";

    /// <inheritdoc />
    public string Kind => KnownEmbeddingKinds.Lexical;

    /// <inheritdoc />
    public IEmbeddingSpace Build(IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var tokenized = documents.Select(document => CountTerms(document)).ToArray();
        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var counts in tokenized)
        {
            foreach (var term in counts.Keys)
            {
                documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }
        }

        // Dimensions follow ordinal term order so an index built twice from the same corpus is identical.
        var vocabulary = documentFrequency.Keys
            .Order(StringComparer.Ordinal)
            .Select((term, dimension) => new EmbeddingTerm(
                term,
                dimension,
                InverseDocumentFrequency(documents.Count, documentFrequency[term])))
            .ToArray();
        var weights = vocabulary.ToDictionary(term => term.Term, StringComparer.Ordinal);
        var vectors = tokenized.Select(counts => Vectorize(counts, weights)).ToArray();
        return new LexicalEmbeddingSpace(vocabulary, vectors);
    }

    /// <inheritdoc />
    public IQueryEmbedder Restore(IReadOnlyList<EmbeddingTerm> vocabulary)
    {
        ArgumentNullException.ThrowIfNull(vocabulary);

        return new LexicalEmbeddingSpace(vocabulary.ToArray(), []);
    }

    // Smoothed so a term present in every document scores above zero rather than vanishing, which keeps a
    // query made entirely of common terms from returning nothing at all.
    private static double InverseDocumentFrequency(int documentCount, int documentFrequency) =>
        Math.Log(1d + ((double)documentCount / documentFrequency));

    // Sublinear term frequency: a term repeated many times in one card is more relevant than a term seen
    // once, but not proportionally so.
    private static EmbeddingVector Vectorize(
        Dictionary<string, int> counts,
        Dictionary<string, EmbeddingTerm> vocabulary)
    {
        var weights = new Dictionary<int, double>(counts.Count);
        foreach (var (term, count) in counts)
        {
            if (!vocabulary.TryGetValue(term, out var entry))
            {
                continue;
            }

            weights[entry.Dimension] = (1d + Math.Log(count)) * entry.InverseDocumentFrequency;
        }

        return EmbeddingVector.Normalize(weights);
    }

    private static Dictionary<string, int> CountTerms(string? text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var term in IdentifierTokenizer.Tokenize(text))
        {
            counts[term] = counts.GetValueOrDefault(term) + 1;
        }

        return counts;
    }

    private sealed class LexicalEmbeddingSpace : IEmbeddingSpace
    {
        private readonly Dictionary<string, EmbeddingTerm> byTerm;

        internal LexicalEmbeddingSpace(EmbeddingTerm[] vocabulary, EmbeddingVector[] documentVectors)
        {
            byTerm = vocabulary.ToDictionary(term => term.Term, StringComparer.Ordinal);
            Vocabulary = new ReadOnlyCollection<EmbeddingTerm>(
                vocabulary.OrderBy(term => term.Dimension).ToArray());
            DocumentVectors = new ReadOnlyCollection<EmbeddingVector>(documentVectors);
        }

        public IReadOnlyList<EmbeddingVector> DocumentVectors { get; }

        public IReadOnlyList<EmbeddingTerm> Vocabulary { get; }

        public EmbeddingVector Embed(string? text) => Vectorize(CountTerms(text), byTerm);
    }
}

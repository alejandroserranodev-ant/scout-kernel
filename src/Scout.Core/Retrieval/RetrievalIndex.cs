using System.Collections.ObjectModel;

namespace Scout.Core.Retrieval;

/// <summary>
/// A built, searchable retrieval corpus, independent of how it is persisted.
/// </summary>
public sealed class RetrievalIndex
{
    private readonly ReadOnlyCollection<RetrievalCard> cards;
    private readonly ReadOnlyCollection<EmbeddingVector> vectors;
    private readonly IQueryEmbedder embedder;

    private RetrievalIndex(
        string modelId,
        string modelKind,
        RetrievalCard[] cards,
        EmbeddingVector[] vectors,
        IReadOnlyList<EmbeddingTerm> vocabulary,
        IQueryEmbedder embedder)
    {
        ModelId = modelId;
        ModelKind = modelKind;
        this.cards = new ReadOnlyCollection<RetrievalCard>(cards);
        this.vectors = new ReadOnlyCollection<EmbeddingVector>(vectors);
        Vocabulary = vocabulary;
        this.embedder = embedder;
    }

    /// <summary>Gets the identifier of the model that built the space.</summary>
    public string ModelId { get; }

    /// <summary>Gets what the space matches. See <see cref="KnownEmbeddingKinds"/>.</summary>
    public string ModelKind { get; }

    /// <summary>Gets the indexed cards, positionally aligned with <see cref="Vectors"/>.</summary>
    public IReadOnlyList<RetrievalCard> Cards => cards;

    /// <summary>Gets the document vectors, positionally aligned with <see cref="Cards"/>.</summary>
    public IReadOnlyList<EmbeddingVector> Vectors => vectors;

    /// <summary>Gets the vocabulary required to embed a query against this space.</summary>
    public IReadOnlyList<EmbeddingTerm> Vocabulary { get; }

    /// <summary>
    /// Gets the optional dense vectors, aligned with <see cref="Cards"/>. Their absence is the default: the
    /// index then answers from its lexical vectors alone.
    /// </summary>
    public DenseIndex? Dense { get; private init; }

    /// <summary>
    /// Gets the typed, directed edges between cards. Empty unless <see cref="WithEdges"/> supplied them.
    /// </summary>
    public IReadOnlyList<RetrievalEdge> Edges { get; private init; } = [];

    /// <summary>Returns this index carrying <paramref name="edges"/>.</summary>
    public RetrievalIndex WithEdges(IReadOnlyList<RetrievalEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return new RetrievalIndex(ModelId, ModelKind, [.. cards], [.. vectors], Vocabulary, embedder) { Dense = Dense, Edges = edges };
    }

    /// <summary>
    /// Builds an index over a corpus.
    /// </summary>
    /// <param name="cards">The corpus.</param>
    /// <param name="model">The model establishing the vector space.</param>
    /// <returns>The searchable index.</returns>
    public static RetrievalIndex Build(IReadOnlyList<RetrievalCard> cards, IEmbeddingModel model)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(model);

        var space = model.Build(cards.Select(card => card.SearchText).ToArray());
        if (space.DocumentVectors.Count != cards.Count)
        {
            throw new InvalidOperationException(
                "An embedding model must return exactly one vector per document.");
        }

        return new RetrievalIndex(
            model.ModelId,
            model.Kind,
            cards.ToArray(),
            space.DocumentVectors.ToArray(),
            space.Vocabulary,
            space);
    }

    /// <summary>
    /// Returns this index with dense vectors attached.
    /// </summary>
    /// <param name="dense">One dense vector per card, in the order of <see cref="Cards"/>.</param>
    /// <returns>The index with its dense vectors.</returns>
    public RetrievalIndex WithDense(DenseIndex dense)
    {
        ArgumentNullException.ThrowIfNull(dense);
        if (dense.Vectors.Count != cards.Count)
        {
            throw new ArgumentException("Dense vectors must be aligned with the cards.", nameof(dense));
        }

        return new RetrievalIndex(ModelId, ModelKind, [.. cards], [.. vectors], Vocabulary, embedder) { Dense = dense, Edges = Edges };
    }

    /// <summary>
    /// Restores an index that was previously persisted.
    /// </summary>
    /// <param name="modelId">The model identifier recorded with the index.</param>
    /// <param name="modelKind">The model kind recorded with the index.</param>
    /// <param name="cards">The persisted cards.</param>
    /// <param name="vectors">The persisted vectors, aligned with the cards.</param>
    /// <param name="embedder">An embedder positioned in the same space.</param>
    /// <returns>The searchable index.</returns>
    public static RetrievalIndex Restore(
        string modelId,
        string modelKind,
        IReadOnlyList<RetrievalCard> cards,
        IReadOnlyList<EmbeddingVector> vectors,
        IQueryEmbedder embedder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKind);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(embedder);

        if (cards.Count != vectors.Count)
        {
            throw new ArgumentException("Cards and vectors must be aligned.", nameof(vectors));
        }

        return new RetrievalIndex(modelId, modelKind, cards.ToArray(), vectors.ToArray(), [], embedder);
    }

    /// <summary>
    /// Searches the corpus.
    /// </summary>
    /// <param name="query">The caller's intent, in their own words.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <param name="kind">An optional card kind to restrict the search to.</param>
    /// <returns>The ranked hits, which is empty when nothing in the query is recognized.</returns>
    public IReadOnlyList<RetrievalHit> Search(string? query, int limit, string? kind = null)
    {
        var vector = embedder.Embed(query);
        var candidates = cards
            .Select((card, index) => (Card: card, Vector: vectors[index]))
            .Where(candidate => kind is null || string.Equals(candidate.Card.Kind, kind, StringComparison.Ordinal));
        return RetrievalRanking.Rank(candidates, vector, limit, query);
    }
}

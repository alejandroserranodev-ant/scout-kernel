using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class DenseRetrievalTests
{
    private static readonly RetrievalCard WaLink = Card("waLink", "symbol", "builds the address for a model", ["Hero", "ModelPanel"]);
    private static readonly RetrievalCard Unrelated = Card("computeTotal", "symbol", "sums the invoice lines", []);
    private static readonly RetrievalCard Business = Card("businessWhatsappLink", "symbol", "link to the sales number", []);
    private static readonly RetrievalCard[] Cards = [WaLink, Unrelated, Business];

    [Fact]
    public async Task HybridSearchFindsACardThatSharesNoWordWithTheQuestion()
    {
        using var scratch = new ScratchDirectory();
        var model = new ConceptDenseModel();
        var path = await WriteAsync(scratch, model).ConfigureAwait(true);

        // "whatsapp", "chat" and "url" appear nowhere in waLink's card: only the semantic channel can connect it to "wa".
        using var lexicalOnly = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel());
        Assert.DoesNotContain(lexicalOnly.Search("whatsapp chat url", 3).Select(hit => hit.Card.Name), name => name == "waLink");

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), model);
        var result = await database.SearchAsync("whatsapp chat url", 3).ConfigureAwait(true);

        Assert.Equal(KnownSearchModes.Hybrid, result.Mode);
        Assert.Null(result.Notice);
        Assert.Contains(result.Hits, hit => hit.Card.Name == "waLink");
    }

    [Fact]
    public async Task ACardBothChannelsAgreeOnOutranksOneOnlyOneLiked()
    {
        using var scratch = new ScratchDirectory();
        var model = new ConceptDenseModel();
        var path = await WriteAsync(scratch, model).ConfigureAwait(true);
        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), model);

        var result = await database.SearchAsync("wa link", 3).ConfigureAwait(true);

        Assert.Equal("waLink", result.Hits[0].Card.Name);
    }

    [Fact]
    public async Task AnIndexWithoutDenseVectorsAnswersLexicallyWithoutANotice()
    {
        using var scratch = new ScratchDirectory();
        var path = Path.Combine(scratch.Path, "plain.db");
        SqliteRetrievalDatabase.Write(path, RetrievalIndex.Build(Cards, new LexicalEmbeddingModel()), null);

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), new ConceptDenseModel());
        var result = await database.SearchAsync("invoice lines", 3).ConfigureAwait(true);

        Assert.Null(database.DenseModelId);
        Assert.Equal(KnownSearchModes.Lexical, result.Mode);
        Assert.Null(result.Notice);
        Assert.Equal("computeTotal", result.Hits[0].Card.Name);
    }

    [Fact]
    public async Task WithoutAModelADenseIndexStillAnswersAndSaysWhatItDidNotUse()
    {
        using var scratch = new ScratchDirectory();
        var path = await WriteAsync(scratch, new ConceptDenseModel()).ConfigureAwait(true);

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel());
        var result = await database.SearchAsync("invoice lines", 3).ConfigureAwait(true);

        Assert.Equal("test:concepts", database.DenseModelId);
        Assert.Equal(KnownSearchModes.Lexical, result.Mode);
        Assert.Contains("test:concepts", result.Notice, StringComparison.Ordinal);
        Assert.Equal("computeTotal", result.Hits[0].Card.Name);
    }

    [Fact]
    public async Task AnUnreachableModelFallsBackToLexicalWithItsOwnMessage()
    {
        using var scratch = new ScratchDirectory();
        var path = await WriteAsync(scratch, new ConceptDenseModel()).ConfigureAwait(true);

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), new ConceptDenseModel(unavailable: true));
        var result = await database.SearchAsync("invoice lines", 3).ConfigureAwait(true);

        Assert.Equal(KnownSearchModes.Lexical, result.Mode);
        Assert.Contains("The test model is not running.", result.Notice, StringComparison.Ordinal);
        Assert.Equal("computeTotal", result.Hits[0].Card.Name);
    }

    [Fact]
    public async Task ADifferentModelIsNeverComparedWithVectorsItDidNotMake()
    {
        using var scratch = new ScratchDirectory();
        var path = await WriteAsync(scratch, new ConceptDenseModel()).ConfigureAwait(true);

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), new ConceptDenseModel("test:other"));
        var result = await database.SearchAsync("invoice lines", 3).ConfigureAwait(true);

        Assert.Equal(KnownSearchModes.Lexical, result.Mode);
        Assert.Contains("test:other", result.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheModelIsChosenFromWhatTheIndexRecordsAndOnlyWhenItHasOne()
    {
        using var scratch = new ScratchDirectory();
        var densePath = await WriteAsync(scratch, new ConceptDenseModel()).ConfigureAwait(true);
        var plainPath = Path.Combine(scratch.Path, "plain.db");
        SqliteRetrievalDatabase.Write(plainPath, RetrievalIndex.Build(Cards, new LexicalEmbeddingModel()), null);

        var asked = new List<string>();
        using (SqliteRetrievalDatabase.Open(densePath, new LexicalEmbeddingModel(), id => { asked.Add(id); return null; }))
        using (SqliteRetrievalDatabase.Open(plainPath, new LexicalEmbeddingModel(), id => { asked.Add(id); return null; }))
        {
        }

        Assert.Equal(["test:concepts"], asked);
    }

    [Fact]
    public async Task SearchRestrictedToAKindOnlyReturnsThatKind()
    {
        using var scratch = new ScratchDirectory();
        var model = new ConceptDenseModel();
        RetrievalCard[] mixed = [WaLink, Card("Hola quiero el link", "text", "text in ModelPanel", ["ModelPanel"]), Unrelated];
        var path = Path.Combine(scratch.Path, "mixed.db");
        var index = RetrievalIndex.Build(mixed, new LexicalEmbeddingModel())
            .WithDense(new DenseIndex(model.ModelId, await model.EmbedAsync(mixed.Select(DenseText.Build).ToArray()).ConfigureAwait(true)));
        SqliteRetrievalDatabase.Write(path, index, null);

        using var database = SqliteRetrievalDatabase.Open(path, new LexicalEmbeddingModel(), model);
        var result = await database.SearchAsync("whatsapp link", 5, KnownCardKinds.Text).ConfigureAwait(true);

        Assert.All(result.Hits, hit => Assert.Equal(KnownCardKinds.Text, hit.Card.Kind));
        Assert.NotEmpty(result.Hits);
    }

    [Fact]
    public void QuantizingAVectorKeepsItsDirection()
    {
        var vector = DenseVectors.Normalize([0.9f, -0.3f, 0.05f, 0.2f, -0.7f, 0.01f]);
        var restored = new float[vector.Length];

        DenseStorage.Decode(DenseStorage.Encode(vector), restored);

        Assert.True(DenseVectors.Dot(vector, DenseVectors.Normalize(restored)) > 0.999);
    }

    [Fact]
    public void FusionRanksByRankNotByScaleAndIsDeterministic()
    {
        var lexical = new[] { Hit(Unrelated, 0.9), Hit(WaLink, 0.5) };
        var semantic = new (RetrievalCard, double)[] { (WaLink, 0.8), (Business, 0.7) };

        var fused = RetrievalRanking.Fuse(lexical, semantic, 3);

        Assert.Equal("waLink", fused[0].Card.Name);
        Assert.Equal(["waLink", "computeTotal", "businessWhatsappLink"], fused.Select(hit => hit.Card.Name));
        Assert.Equal(fused.Select(hit => hit.Card.Id), RetrievalRanking.Fuse(lexical, semantic, 3).Select(hit => hit.Card.Id));
        Assert.All(fused, hit => Assert.InRange(hit.Score, 0, 1.5));
    }

    [Fact]
    public async Task CachedModelEmbedsEachTextOnceAndReusesItAfterwards()
    {
        var inner = new ConceptDenseModel();
        var model = new CachedDenseEmbeddingModel(inner, new MemoryCache());

        var first = await model.EmbedAsync(["wa link", "name", "wa link"]).ConfigureAwait(true);
        Assert.Equal(2, model.Embedded + 0);
        Assert.Equal(1, model.Reused);

        var second = await model.EmbedAsync(["name", "wa link"]).ConfigureAwait(true);

        Assert.Equal(0, model.Embedded);
        Assert.Equal(2, model.Reused);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(first[1], second[0]);
    }

    [Fact]
    public void SqliteCacheKeepsVectorsApartByModel()
    {
        using var scratch = new ScratchDirectory();
        var cache = new SqliteDenseEmbeddingCache(Path.Combine(scratch.Path, "cache.db"));

        cache.PutMany("model-a", new Dictionary<string, float[]> { ["h1"] = [0.5f, 0.25f] });

        Assert.Equal([0.5f, 0.25f], cache.GetMany("model-a", ["h1", "h2"])["h1"]);
        Assert.Empty(cache.GetMany("model-b", ["h1"]));
        Assert.DoesNotContain("h2", cache.GetMany("model-a", ["h1", "h2"]).Keys);
    }

    private static async Task<string> WriteAsync(ScratchDirectory scratch, ConceptDenseModel model)
    {
        var vectors = await model.EmbedAsync(Cards.Select(DenseText.Build).ToArray()).ConfigureAwait(true);
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel()).WithDense(new DenseIndex(model.ModelId, vectors));
        var path = Path.Combine(scratch.Path, "dense.db");
        SqliteRetrievalDatabase.Write(path, index, null);
        return path;
    }

    private static RetrievalCard Card(string name, string kind, string summary, string[] related) =>
        new($"{kind}:{name}", kind, name, "src/a.ts", summary, 0.2, related: related);

    private static RetrievalHit Hit(RetrievalCard card, double similarity) => new(card, similarity, similarity);

    private sealed class MemoryCache : IDenseEmbeddingCache
    {
        private readonly Dictionary<(string, string), float[]> store = [];

        public IReadOnlyDictionary<string, float[]> GetMany(string modelId, IReadOnlyCollection<string> textHashes) =>
            textHashes
                .Where(hash => store.ContainsKey((modelId, hash)))
                .ToDictionary(hash => hash, hash => store[(modelId, hash)], StringComparer.Ordinal);

        public void PutMany(string modelId, IReadOnlyDictionary<string, float[]> vectors)
        {
            foreach (var (hash, vector) in vectors)
            {
                store[(modelId, hash)] = vector;
            }
        }
    }

    private sealed class ScratchDirectory : IDisposable
    {
        public ScratchDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"scout-dense-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

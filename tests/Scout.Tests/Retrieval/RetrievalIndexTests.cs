using Scout.Core.Retrieval;

namespace Scout.Tests.Retrieval;

public sealed class RetrievalIndexTests
{
    private static readonly RetrievalCard[] Cards =
    [
        new(
            "contract-1",
            KnownCardKinds.Contract,
            "formatTotal",
            "src/invoiceStore.js",
            "callable-surface with 2 consumers. Changing its shape affects every listed consumer.",
            importance: 1.0,
            facets: [new RetrievalFacet("blastRadius", "2")],
            related: ["InvoiceRow", "InvoiceTable"],
            detail: "contracts/contract-1.json"),
        new(
            "module-1",
            KnownCardKinds.Module,
            "checkout",
            "src/checkout",
            "Checkout module projected from one source project.",
            importance: 0.6),
        new(
            "symbol-1",
            KnownCardKinds.Symbol,
            "zeroed",
            "src/invoiceStore.js",
            "private function 'zeroed', declared at src/invoiceStore.js:20.",
            importance: 0.2)
    ];

    [Fact]
    public void RanksTheCardWhoseWordingMatchesTheQuery()
    {
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());

        var hits = index.Search("format the invoice total", limit: 3);

        Assert.NotEmpty(hits);
        Assert.Equal("contract-1", hits[0].Card.Id);
    }

    [Fact]
    public void FindsAContractThroughTheNameOfOneOfItsConsumers()
    {
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());

        var hits = index.Search("InvoiceTable", limit: 3);

        Assert.Equal("contract-1", hits[0].Card.Id);
    }

    [Fact]
    public void RestrictsResultsToTheRequestedKind()
    {
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());

        var hits = index.Search("invoice", limit: 5, kind: KnownCardKinds.Symbol);

        Assert.All(hits, hit => Assert.Equal(KnownCardKinds.Symbol, hit.Card.Kind));
    }

    [Fact]
    public void ReturnsNothingWhenNoQueryTermIsInTheVocabulary()
    {
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());

        Assert.Empty(index.Search("kubernetes helm chart", limit: 5));
    }

    [Fact]
    public void BuildsTheSameVocabularyForTheSameCorpus()
    {
        var first = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());
        var second = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());

        Assert.Equal(
            first.Vocabulary.Select(term => (term.Term, term.Dimension, term.InverseDocumentFrequency)),
            second.Vocabulary.Select(term => (term.Term, term.Dimension, term.InverseDocumentFrequency)));
    }

    [Fact]
    public void SurvivesARoundTripThroughTheDatabase()
    {
        var index = RetrievalIndex.Build(Cards, new LexicalEmbeddingModel());
        var expected = index.Search("format the invoice total", limit: 3);
        var databasePath = Path.Combine(Path.GetTempPath(), $"scout-test-{Guid.NewGuid():N}.db");

        try
        {
            Scout.Retrieval.Sqlite.SqliteRetrievalDatabase.Write(databasePath, index, "fingerprint");
            using var database = Scout.Retrieval.Sqlite.SqliteRetrievalDatabase.Open(
                databasePath,
                new LexicalEmbeddingModel());

            Assert.Equal(Cards.Length, database.CardCount);
            Assert.Equal("fingerprint", database.SourceFingerprint);

            var actual = database.Search("format the invoice total", limit: 3);
            Assert.Equal(
                expected.Select(hit => hit.Card.Id),
                actual.Select(hit => hit.Card.Id));
            Assert.Equal(
                expected.Select(hit => Math.Round(hit.Score, 6)),
                actual.Select(hit => Math.Round(hit.Score, 6)));
            Assert.Equal("contracts/contract-1.json", actual[0].Card.Detail);
            Assert.Equal(["InvoiceRow", "InvoiceTable"], actual[0].Card.Related);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}

using Scout.Core.Retrieval;

namespace Scout.Tests.Retrieval;

public sealed class ExactNameRankingTests
{
    private static RetrievalCard Card(string name) =>
        new($"id-{name}", KnownCardKinds.Symbol, name, $"src/{name}.cs", $"symbol {name}", importance: 0);

    [Fact]
    public void ACardNamedExactlyLikeTheQueryOutranksAMarginallyBetterNeighbour()
    {
        var candidates = new[] { (Card("Model"), 0.8143), (Card("ModelHero"), 0.8091) };

        var withoutName = RetrievalRanking.Rank(candidates, limit: 2);
        var withName = RetrievalRanking.Rank(candidates, limit: 2, exactName: "modelhero");

        Assert.Equal("Model", withoutName[0].Card.Name);
        Assert.Equal("ModelHero", withName[0].Card.Name);
    }

    [Fact]
    public void ABlankNameChangesNothing()
    {
        var candidates = new[] { (Card("Model"), 0.8), (Card("ModelHero"), 0.7) };

        Assert.Equal("Model", RetrievalRanking.Rank(candidates, limit: 2, exactName: "  ")[0].Card.Name);
    }
}

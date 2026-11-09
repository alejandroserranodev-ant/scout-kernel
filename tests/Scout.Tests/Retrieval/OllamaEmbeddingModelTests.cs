using System.Net;
using System.Text;
using System.Text.Json;
using Scout.Core.Retrieval;
using Scout.Embeddings.Ollama;

namespace Scout.Tests.Retrieval;

public sealed class OllamaEmbeddingModelTests
{
    private static readonly float[][] OneVector = [[1f]];

    [Fact]
    public async Task EmbedSendsBatchesAndReturnsUnitVectorsInOrder()
    {
        var handler = new FakeHandler(request =>
        {
            var texts = request.GetProperty("input").EnumerateArray().Select(item => item.GetString()!).ToArray();
            return Json(new { embeddings = texts.Select(text => new[] { (float)text.Length, 0f }).ToArray() });
        });
        var model = Create(handler, "bge-m3");

        var vectors = await model.EmbedAsync(Enumerable.Range(0, 70).Select(index => new string('x', index + 1)).ToArray())
            .ConfigureAwait(true);

        Assert.Equal(70, vectors.Count);
        Assert.Equal([32, 32, 6], handler.BatchSizes);
        Assert.All(vectors, vector => Assert.Equal(1d, DenseVectors.Dot(vector, vector), 5));
        Assert.Equal("ollama:bge-m3", model.ModelId);
        Assert.All(handler.Models, name => Assert.Equal("bge-m3", name));
    }

    [Fact]
    public async Task AMissingModelTellsTheUserHowToGetIt()
    {
        var model = Create(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)), "bge-m3");

        var failure = await Assert.ThrowsAsync<DenseEmbeddingUnavailableException>(() => model.EmbedAsync(["a"]))
            .ConfigureAwait(true);

        Assert.Contains("ollama pull bge-m3", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreachableServerSaysSoAndNamesTheAlternative()
    {
        var model = Create(new FakeHandler(_ => throw new HttpRequestException("refused")), "bge-m3");

        var failure = await Assert.ThrowsAsync<DenseEmbeddingUnavailableException>(() => model.EmbedAsync(["a"]))
            .ConfigureAwait(true);

        Assert.Contains("not reachable", failure.Message, StringComparison.Ordinal);
        Assert.Contains("ollama serve", failure.Message, StringComparison.Ordinal);
        Assert.Contains("lexical", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerWithTheWrongNumberOfVectorsIsRejected()
    {
        var model = Create(new FakeHandler(_ => Json(new { embeddings = OneVector })), "bge-m3");

        await Assert.ThrowsAsync<DenseEmbeddingUnavailableException>(() => model.EmbedAsync(["a", "b"]))
            .ConfigureAwait(true);
    }

    [Theory]
    [InlineData("ollama", "ollama:bge-m3")]
    [InlineData("OLLAMA", "ollama:bge-m3")]
    [InlineData("ollama:nomic-embed-text", "ollama:nomic-embed-text")]
    [InlineData("ollama:qwen3-embedding:0.6b", "ollama:qwen3-embedding:0.6b")]
    public void TryParseReadsAProviderAndAModel(string specification, string expectedId)
    {
        Assert.Equal(expectedId, OllamaEmbeddingModel.TryParse(specification, new HttpClient(new FakeHandler(_ => Json(new { })))
        {
            BaseAddress = new Uri("http://localhost:11434")
        })?.ModelId);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("ollama:")]
    [InlineData("")]
    public void TryParseLeavesOtherProvidersAlone(string specification) =>
        Assert.Null(OllamaEmbeddingModel.TryParse(specification));

    private static OllamaEmbeddingModel Create(FakeHandler handler, string name) =>
        new(name, new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") });

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<JsonElement, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<int> BatchSizes { get; } = [];

        public List<string> Models { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("/api/embed", request.RequestUri!.AbsolutePath);
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).RootElement.Clone();
            BatchSizes.Add(body.GetProperty("input").GetArrayLength());
            Models.Add(body.GetProperty("model").GetString()!);
            return respond(body);
        }
    }
}

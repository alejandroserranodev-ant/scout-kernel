using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Scout.Core.Retrieval;

namespace Scout.Embeddings.Ollama;

/// <summary>
/// A dense embedding model served by a local Ollama instance.
/// </summary>
/// <remarks>
/// Ollama runs the model on the machine that asks, so no text leaves it and no key is needed. It is one possible
/// implementation of <see cref="IDenseEmbeddingModel"/>: nothing in Scout depends on it, and a machine without
/// Ollama compiles and queries lexically.
/// </remarks>
public sealed class OllamaEmbeddingModel : IDenseEmbeddingModel
{
    /// <summary>The model used when none is named: multilingual, so Spanish and English identifiers share a space.</summary>
    public const string DefaultModel = "bge-m3";

    /// <summary>The prefix of the identifiers this adapter produces and recognizes.</summary>
    public const string IdPrefix = "ollama:";

    private const int BatchSize = 32;
    private const string DefaultEndpoint = "http://localhost:11434";

    private readonly HttpClient client;
    private readonly string model;

    /// <summary>
    /// Initializes the model.
    /// </summary>
    /// <param name="model">The Ollama model name, such as <c>bge-m3</c>.</param>
    /// <param name="client">
    /// The HTTP client to use, with its base address set. When omitted, a client for the local Ollama endpoint is
    /// created from <c>OLLAMA_HOST</c>, as Ollama's own tools do.
    /// </param>
    public OllamaEmbeddingModel(string model = DefaultModel, HttpClient? client = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        this.model = model.Trim();
        this.client = client ?? CreateClient();
    }

    /// <inheritdoc />
    public string ModelId => IdPrefix + model;

    /// <summary>
    /// Reads a model from the text that names it on the command line or in an index.
    /// </summary>
    /// <param name="specification"><c>ollama</c>, or <c>ollama:&lt;model&gt;</c>.</param>
    /// <param name="client">An optional HTTP client, for tests.</param>
    /// <returns>The model, or <see langword="null"/> when the text names another provider.</returns>
    public static OllamaEmbeddingModel? TryParse(string specification, HttpClient? client = null)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var text = specification.Trim();
        if (string.Equals(text, "ollama", StringComparison.OrdinalIgnoreCase))
        {
            return new OllamaEmbeddingModel(DefaultModel, client);
        }

        return text.StartsWith(IdPrefix, StringComparison.OrdinalIgnoreCase) && text.Length > IdPrefix.Length
            ? new OllamaEmbeddingModel(text[IdPrefix.Length..], client)
            : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = new List<float[]>(texts.Count);
        foreach (var batch in texts.Chunk(BatchSize))
        {
            vectors.AddRange(await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false));
        }

        return vectors;
    }

    private static HttpClient CreateClient()
    {
        var host = Environment.GetEnvironmentVariable("OLLAMA_HOST");
        var endpoint = string.IsNullOrWhiteSpace(host) ? DefaultEndpoint : NormalizeHost(host.Trim());
        return new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromMinutes(5) };
    }

    // OLLAMA_HOST is commonly a bare "host:port", sometimes "0.0.0.0", which names where a server listens
    // rather than where a client should call.
    private static string NormalizeHost(string host)
    {
        var withScheme = host.Contains("://", StringComparison.Ordinal) ? host : $"http://{host}";
        return withScheme.Replace("//0.0.0.0", "//localhost", StringComparison.Ordinal);
    }

    private async Task<IReadOnlyList<float[]>> EmbedBatchAsync(string[] batch, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await client
                .PostAsJsonAsync("/api/embed", new EmbedRequest(model, batch), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new DenseEmbeddingUnavailableException(
                $"Ollama is not reachable at {client.BaseAddress}. Start it with 'ollama serve', or leave out --embeddings to use the lexical index.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DenseEmbeddingUnavailableException(
                $"Ollama did not answer in time at {client.BaseAddress}.",
                exception);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new DenseEmbeddingUnavailableException(
                    $"Ollama does not have the model '{model}'. Download it with 'ollama pull {model}'.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DenseEmbeddingUnavailableException(
                    $"Ollama answered {(int)response.StatusCode} {response.ReasonPhrase} while embedding with '{model}'.");
            }

            var body = await response.Content
                .ReadFromJsonAsync<EmbedResponse>(cancellationToken)
                .ConfigureAwait(false);
            if (body?.Embeddings is null || body.Embeddings.Length != batch.Length)
            {
                throw new DenseEmbeddingUnavailableException(
                    $"Ollama returned {body?.Embeddings?.Length ?? 0} vectors for {batch.Length} texts.");
            }

            return body.Embeddings.Select(vector => DenseVectors.Normalize(vector)).ToArray();
        }
    }

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string[] Input);

    private sealed record EmbedResponse([property: JsonPropertyName("embeddings")] float[][]? Embeddings);
}

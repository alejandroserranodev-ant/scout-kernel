using Scout.Core.Retrieval;

namespace Scout.Tests.Support;

/// <summary>
/// A stand-in dense model that places words of one concept on one axis, so tests can ask for the behaviour of a
/// semantic model (an abbreviation and its long form land together) without a real model.
/// </summary>
internal sealed class ConceptDenseModel(string modelId = "test:concepts", bool unavailable = false) : IDenseEmbeddingModel
{
    private static readonly string[][] Concepts =
    [
        ["wa", "whatsapp", "wsp"],
        ["link", "url", "enlace"],
        ["version", "edicion", "variant"],
        ["subtitle", "tagline", "eyebrow", "subtitulo"],
        ["name", "nombre"],
        ["business", "sales", "ventas"]
    ];

    private int calls;

    public string ModelId { get; } = modelId;

    public int Calls => calls;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref calls);
        if (unavailable)
        {
            throw new DenseEmbeddingUnavailableException("The test model is not running.");
        }

        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToArray());
    }

    private static float[] Embed(string text)
    {
        var words = IdentifierTokenizer.Tokenize(text).ToHashSet(StringComparer.Ordinal);
        var vector = new float[Concepts.Length + 1];
        for (var axis = 0; axis < Concepts.Length; axis++)
        {
            vector[axis] = Concepts[axis].Count(words.Contains);
        }

        // A small constant keeps a text that mentions no concept from being the zero vector.
        vector[^1] = 0.1f;
        return DenseVectors.Normalize(vector);
    }
}

namespace Scout.Core.Retrieval;

/// <summary>
/// The dense vectors of a retrieval index, aligned card for card with the index they belong to.
/// </summary>
public sealed class DenseIndex
{
    /// <summary>
    /// Initializes a dense index.
    /// </summary>
    /// <param name="modelId">The model that produced the vectors.</param>
    /// <param name="vectors">One normalized vector per card, all of the same dimension.</param>
    public DenseIndex(string modelId, IReadOnlyList<float[]> vectors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(vectors);

        var dimensions = vectors.Count == 0 ? 0 : vectors[0].Length;
        if (vectors.Any(vector => vector.Length != dimensions))
        {
            throw new ArgumentException("Every dense vector must have the same dimension.", nameof(vectors));
        }

        ModelId = modelId;
        Dimensions = dimensions;
        Vectors = vectors;
    }

    /// <summary>Gets the model that produced the vectors.</summary>
    public string ModelId { get; }

    /// <summary>Gets the dimension shared by every vector.</summary>
    public int Dimensions { get; }

    /// <summary>Gets the vectors, in the order of the index's cards.</summary>
    public IReadOnlyList<float[]> Vectors { get; }
}

/// <summary>
/// Arithmetic on dense vectors.
/// </summary>
public static class DenseVectors
{
    /// <summary>
    /// Scales a vector to unit length.
    /// </summary>
    /// <param name="vector">The vector to normalize.</param>
    /// <returns>A new vector of unit length, or a copy of the input when it has no magnitude.</returns>
    public static float[] Normalize(IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(vector);

        var magnitude = 0d;
        foreach (var value in vector)
        {
            magnitude += (double)value * value;
        }

        magnitude = Math.Sqrt(magnitude);
        var result = new float[vector.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = magnitude <= 0 ? vector[index] : (float)(vector[index] / magnitude);
        }

        return result;
    }

    /// <summary>
    /// Computes the dot product of two vectors, which is their cosine similarity when both are normalized.
    /// </summary>
    /// <param name="left">The first vector.</param>
    /// <param name="right">The second vector, of the same dimension.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Dense vectors of different dimensions cannot be compared.");
        }

        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            sum += (double)left[index] * right[index];
        }

        return sum;
    }
}

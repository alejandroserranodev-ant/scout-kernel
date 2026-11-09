namespace Scout.Core.Retrieval;

/// <summary>
/// A single non-zero coordinate of an <see cref="EmbeddingVector"/>.
/// </summary>
/// <param name="Dimension">The zero-based dimension index.</param>
/// <param name="Weight">The normalized weight at that dimension.</param>
public readonly record struct EmbeddingComponent(int Dimension, double Weight);

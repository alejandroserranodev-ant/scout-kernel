using System.Collections.ObjectModel;

namespace Scout.Core.Retrieval;

/// <summary>
/// A unit-length vector expressed as its non-zero components, so sparse lexical models and dense semantic
/// models can share one storage and scoring shape.
/// </summary>
public sealed class EmbeddingVector
{
    private readonly ReadOnlyCollection<EmbeddingComponent> components;

    private EmbeddingVector(EmbeddingComponent[] components)
    {
        this.components = new ReadOnlyCollection<EmbeddingComponent>(components);
    }

    /// <summary>Gets the empty vector, which is orthogonal to every other vector.</summary>
    public static EmbeddingVector Empty { get; } = new([]);

    /// <summary>Gets the non-zero components, ordered by dimension.</summary>
    public IReadOnlyList<EmbeddingComponent> Components => components;

    /// <summary>Gets a value indicating whether the vector carries no signal.</summary>
    public bool IsEmpty => components.Count == 0;

    /// <summary>
    /// Creates a unit-length vector from raw component weights.
    /// </summary>
    /// <param name="weights">Weights by dimension. Non-positive weights are discarded.</param>
    /// <returns>The normalized vector, or <see cref="Empty"/> when no weight survives.</returns>
    public static EmbeddingVector Normalize(IEnumerable<KeyValuePair<int, double>> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        var retained = weights.Where(weight => weight.Value > 0).ToArray();
        if (retained.Length == 0)
        {
            return Empty;
        }

        var magnitude = Math.Sqrt(retained.Sum(weight => weight.Value * weight.Value));
        if (magnitude <= 0)
        {
            return Empty;
        }

        var normalized = retained
            .Select(weight => new EmbeddingComponent(weight.Key, weight.Value / magnitude))
            .OrderBy(component => component.Dimension)
            .ToArray();
        return new EmbeddingVector(normalized);
    }

    /// <summary>
    /// Restores a vector whose components were already normalized before persistence.
    /// </summary>
    /// <param name="components">The persisted components.</param>
    /// <returns>The restored vector.</returns>
    public static EmbeddingVector FromNormalizedComponents(IEnumerable<EmbeddingComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var ordered = components
            .Where(component => component.Weight != 0)
            .OrderBy(component => component.Dimension)
            .ToArray();
        return ordered.Length == 0 ? Empty : new EmbeddingVector(ordered);
    }

    /// <summary>
    /// Computes cosine similarity. Both operands are unit length, so this is their dot product.
    /// </summary>
    /// <param name="other">The vector to compare against.</param>
    /// <returns>A similarity in the range zero through one.</returns>
    public double SimilarityTo(EmbeddingVector other)
    {
        ArgumentNullException.ThrowIfNull(other);

        var score = 0d;
        var left = 0;
        var right = 0;
        while (left < components.Count && right < other.components.Count)
        {
            var leftComponent = components[left];
            var rightComponent = other.components[right];
            if (leftComponent.Dimension == rightComponent.Dimension)
            {
                score += leftComponent.Weight * rightComponent.Weight;
                left++;
                right++;
            }
            else if (leftComponent.Dimension < rightComponent.Dimension)
            {
                left++;
            }
            else
            {
                right++;
            }
        }

        return score;
    }
}

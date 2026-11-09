using System.Globalization;

namespace Scout.Core.Ir;

/// <summary>
/// Represents a normalized confidence value between zero and one.
/// </summary>
public readonly record struct ConfidenceScore
{
    /// <summary>
    /// Initializes a confidence score.
    /// </summary>
    /// <param name="value">A finite value in the inclusive range from zero to one.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside the valid range.</exception>
    public ConfidenceScore(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Confidence must be between zero and one.");
        }

        Value = value;
    }

    /// <summary>
    /// Gets a score representing complete confidence.
    /// </summary>
    public static ConfidenceScore Certain { get; } = new(1);

    /// <summary>
    /// Gets a score representing no confidence.
    /// </summary>
    public static ConfidenceScore None { get; } = new(0);

    /// <summary>
    /// Gets the normalized numeric value.
    /// </summary>
    public double Value { get; }

    /// <summary>
    /// Returns the invariant-culture numeric representation.
    /// </summary>
    /// <returns>The numeric representation.</returns>
    public override string ToString() => Value.ToString("0.####", CultureInfo.InvariantCulture);
}

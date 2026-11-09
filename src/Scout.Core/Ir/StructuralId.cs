using System.Security.Cryptography;
using System.Text;

namespace Scout.Core.Ir;

/// <summary>
/// Identifies a structural fact with a stable, analyzer-independent string value.
/// </summary>
public readonly record struct StructuralId : IComparable<StructuralId>
{
    /// <summary>
    /// Initializes a structural identifier from an existing stable value.
    /// </summary>
    /// <param name="value">The non-empty serialized identifier.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value"/> is empty.</exception>
    public StructuralId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A structural identifier cannot be empty.", nameof(value));
        }

        Value = value.Trim().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Gets the serialized identifier value.
    /// </summary>
    public string Value { get; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this instance is the uninitialized default value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Creates a deterministic identifier from normalized relative identity components.
    /// </summary>
    /// <param name="category">The extensible category prefix, such as <c>project</c> or <c>symbol</c>.</param>
    /// <param name="identityParts">The repository-relative or semantic identity components.</param>
    /// <returns>A stable SHA-256-backed identifier.</returns>
    public static StructuralId Create(string category, params string[] identityParts)
    {
        ArgumentNullException.ThrowIfNull(identityParts);
        return Create(category, (IEnumerable<string>)identityParts);
    }

    /// <summary>
    /// Creates a deterministic identifier from normalized relative identity components.
    /// </summary>
    /// <param name="category">The extensible category prefix, such as <c>project</c> or <c>symbol</c>.</param>
    /// <param name="identityParts">The repository-relative or semantic identity components.</param>
    /// <returns>A stable SHA-256-backed identifier.</returns>
    public static StructuralId Create(string category, IEnumerable<string> identityParts)
    {
        ArgumentNullException.ThrowIfNull(identityParts);

        var normalizedCategory = IrGuard.NormalizeKind(category, nameof(category));
        var normalizedParts = identityParts
            .Select((part, index) => NormalizeIdentityPart(part, index))
            .ToArray();

        if (normalizedParts.Length == 0)
        {
            throw new ArgumentException("At least one identity component is required.", nameof(identityParts));
        }

        var canonicalIdentity = BuildLengthPrefixedIdentity(normalizedParts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentity));
        return new StructuralId($"{normalizedCategory}:{Convert.ToHexStringLower(hash)}");
    }

    /// <summary>
    /// Compares this identifier with another identifier using ordinal string ordering.
    /// </summary>
    /// <param name="other">The other identifier.</param>
    /// <returns>A value indicating the relative sort order.</returns>
    public int CompareTo(StructuralId other) => StringComparer.Ordinal.Compare(Value, other.Value);

    /// <summary>
    /// Determines whether one identifier sorts before another identifier.
    /// </summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts before <paramref name="right"/>.</returns>
    public static bool operator <(StructuralId left, StructuralId right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether one identifier sorts after another identifier.
    /// </summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> sorts after <paramref name="right"/>.</returns>
    public static bool operator >(StructuralId left, StructuralId right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether one identifier sorts before or at the same position as another identifier.
    /// </summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort after <paramref name="right"/>.</returns>
    public static bool operator <=(StructuralId left, StructuralId right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether one identifier sorts after or at the same position as another identifier.
    /// </summary>
    /// <param name="left">The left identifier.</param>
    /// <param name="right">The right identifier.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> does not sort before <paramref name="right"/>.</returns>
    public static bool operator >=(StructuralId left, StructuralId right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Returns the serialized identifier value.
    /// </summary>
    /// <returns>The identifier value.</returns>
    public override string ToString() => Value;

    private static string NormalizeIdentityPart(string part, int index)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            throw new ArgumentException($"Identity component {index} cannot be empty.", nameof(part));
        }

        var normalized = part.Trim().Normalize(NormalizationForm.FormC).Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static string BuildLengthPrefixedIdentity(IEnumerable<string> identityParts)
    {
        var builder = new StringBuilder();
        foreach (var identityPart in identityParts)
        {
            var byteCount = Encoding.UTF8.GetByteCount(identityPart);
            builder.Append(byteCount).Append(':').Append(identityPart).Append(';');
        }

        return builder.ToString();
    }
}

using System.Collections.ObjectModel;
using System.Text;

namespace Scout.Core.Ir;

internal static class IrGuard
{
    public static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value cannot be empty.", parameterName);
        }

        return value.Trim().Normalize(NormalizationForm.FormC);
    }

    public static string NormalizeKind(string value, string parameterName)
    {
        var normalized = Required(value, parameterName).ToLowerInvariant();
        if (!char.IsLetterOrDigit(normalized[0]) ||
            normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.' and not ':'))
        {
            throw new ArgumentException(
                "A kind must start with a letter or digit and contain only letters, digits, dots, colons, hyphens, or underscores.",
                parameterName);
        }

        return normalized;
    }

    public static IReadOnlyList<T> CopyOrdered<T>(
        IEnumerable<T>? values,
        IComparer<T> comparer,
        bool distinct = false)
    {
        var array = values?.ToArray() ?? [];
        Array.Sort(array, comparer);

        if (distinct && array.Length > 1)
        {
            array = array.Distinct().ToArray();
        }

        return new ReadOnlyCollection<T>(array);
    }
}

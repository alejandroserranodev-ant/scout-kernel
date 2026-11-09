using System.Globalization;
using System.Text;

namespace Scout.Core.Text;

/// <summary>
/// Reduces accented letters to their base letter so words compare the way a reader would match them.
/// </summary>
/// <remarks>
/// Documentation is written in the project's own language, so a query for "activacion" has to meet a
/// heading named "Activación". Only combining marks are removed; every other character is untouched.
/// </remarks>
internal static class DiacriticFolding
{
    /// <summary>
    /// Removes combining marks from a text.
    /// </summary>
    /// <param name="text">The text to fold.</param>
    /// <returns>The text with accented letters reduced to their base letters.</returns>
    internal static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (Ascii.IsValid(text))
        {
            return text;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}

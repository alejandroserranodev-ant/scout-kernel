using Scout.Core.Ir;

namespace Scout.Core.Analysis;

/// <summary>
/// Builds the identifier of a symbol that one analyzer observes being used but another analyzer declares.
/// </summary>
/// <remarks>
/// <para>
/// Some references cross language boundaries. A React component naming a style class in <c>className</c> is
/// observed by the TypeScript analyzer, while the declaration of that class is observed by the CSS analyzer.
/// Neither analyzer may read the other language, so they agree on a shared vocabulary instead: both derive the
/// same identifier from the same logical key.
/// </para>
/// <para>
/// This type carries no knowledge of any language. The vocabulary is an opaque string owned by the analyzers
/// that agree on it. When the declaring analyzer does not run, the resulting reference stays unresolved and
/// <see cref="Scout.Core.Compilation.ScoutCompiler"/> drops it with an explicit diagnostic rather than
/// failing the compilation or inventing a declaration.
/// </para>
/// </remarks>
public static class SharedSymbolKey
{
    /// <summary>
    /// The prefix analyzers use to mark a relationship endpoint as a shared, cross-analyzer symbol.
    /// </summary>
    public const string Prefix = "shared";

    private const string Separator = "::";

    /// <summary>
    /// Builds the logical key of a shared symbol.
    /// </summary>
    /// <param name="vocabulary">The vocabulary the participating analyzers agreed on, such as <c>css.class</c>.</param>
    /// <param name="name">The declared name within that vocabulary.</param>
    /// <returns>The stable logical key.</returns>
    public static string Create(string vocabulary, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vocabulary);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.Concat(Prefix, Separator, vocabulary.Trim(), Separator, name.Trim());
    }

    /// <summary>
    /// Builds the structural identifier of a shared symbol.
    /// </summary>
    /// <param name="vocabulary">The vocabulary the participating analyzers agreed on, such as <c>css.class</c>.</param>
    /// <param name="name">The declared name within that vocabulary.</param>
    /// <returns>The identifier both analyzers derive independently.</returns>
    public static StructuralId CreateId(string vocabulary, string name)
    {
        return StructuralId.Create(KnownEntityKinds.Symbol, Create(vocabulary, name));
    }

    /// <summary>
    /// Determines whether a logical key denotes a shared, cross-analyzer symbol.
    /// </summary>
    /// <param name="key">The logical key emitted by an analyzer.</param>
    /// <returns><see langword="true"/> when the key uses the shared vocabulary prefix.</returns>
    public static bool IsSharedKey(string? key)
    {
        return key is not null && key.StartsWith(Prefix + Separator, StringComparison.Ordinal);
    }
}

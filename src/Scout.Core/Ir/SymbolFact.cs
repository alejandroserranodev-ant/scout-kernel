namespace Scout.Core.Ir;

/// <summary>
/// Describes an analyzer-observed source language symbol using language-neutral and native classifications.
/// </summary>
public sealed class SymbolFact : StructuralEntity
{
    /// <summary>
    /// Initializes a source symbol fact.
    /// </summary>
    /// <param name="id">The stable symbol identifier.</param>
    /// <param name="name">The symbol name.</param>
    /// <param name="languageId">The extensible language identifier.</param>
    /// <param name="symbolKind">The extensible language-neutral symbol kind.</param>
    /// <param name="visibility">The extensible language-neutral declared visibility.</param>
    /// <param name="nativeKind">The optional analyzer-native symbol kind.</param>
    /// <param name="qualifiedName">The optional qualified symbol name.</param>
    /// <param name="signature">The optional stable semantic signature.</param>
    /// <param name="declarationLocations">The source declaration locations.</param>
    /// <param name="evidenceIds">The evidence identifiers supporting the symbol fact.</param>
    public SymbolFact(
        StructuralId id,
        string name,
        string languageId,
        string symbolKind,
        string? visibility = null,
        string? nativeKind = null,
        string? qualifiedName = null,
        string? signature = null,
        IEnumerable<SourceLocation>? declarationLocations = null,
        IEnumerable<StructuralId>? evidenceIds = null)
        : base(id, KnownEntityKinds.Symbol, name, evidenceIds)
    {
        LanguageId = IrGuard.NormalizeKind(languageId, nameof(languageId));
        SymbolKind = IrGuard.NormalizeKind(symbolKind, nameof(symbolKind));
        Visibility = string.IsNullOrWhiteSpace(visibility)
            ? KnownVisibilities.Unknown
            : IrGuard.NormalizeKind(visibility, nameof(visibility));
        NativeKind = string.IsNullOrWhiteSpace(nativeKind) ? null : nativeKind.Trim();
        QualifiedName = string.IsNullOrWhiteSpace(qualifiedName) ? null : qualifiedName.Trim();
        Signature = string.IsNullOrWhiteSpace(signature) ? null : signature.Trim();
        DeclarationLocations = IrGuard.CopyOrdered(
            declarationLocations,
            SourceLocation.DeterministicComparer);
    }

    /// <summary>
    /// Gets the extensible language identifier.
    /// </summary>
    public string LanguageId { get; }

    /// <summary>
    /// Gets the extensible language-neutral symbol kind.
    /// </summary>
    public string SymbolKind { get; }

    /// <summary>
    /// Gets the extensible language-neutral declared visibility.
    /// </summary>
    public string Visibility { get; }

    /// <summary>
    /// Gets the optional analyzer-native symbol kind.
    /// </summary>
    public string? NativeKind { get; }

    /// <summary>
    /// Gets the optional qualified symbol name.
    /// </summary>
    public string? QualifiedName { get; }

    /// <summary>
    /// Gets the optional stable semantic signature.
    /// </summary>
    public string? Signature { get; }

    /// <summary>
    /// Gets the deterministically ordered declaration locations.
    /// </summary>
    public IReadOnlyList<SourceLocation> DeclarationLocations { get; }
}

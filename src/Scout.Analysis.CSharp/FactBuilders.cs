using Microsoft.CodeAnalysis;
using Scout.Core.Ir;

namespace Scout.Analysis.CSharp;

internal sealed class MutableSymbolFact
{
    private readonly Dictionary<string, SourceLocation> locations = new(StringComparer.Ordinal);
    private readonly HashSet<StructuralId> evidenceIds = [];

    public MutableSymbolFact(
        StructuralId id,
        string name,
        string symbolKind,
        string visibility,
        string nativeKind,
        string qualifiedName,
        string signature,
        string assemblyName)
    {
        Id = id;
        Name = name;
        SymbolKind = symbolKind;
        Visibility = visibility;
        NativeKind = nativeKind;
        QualifiedName = qualifiedName;
        Signature = signature;
        AssemblyName = assemblyName;
        EvidenceId = StructuralId.Create("evidence", CSharpLanguageAnalyzerId.Value, "declaration", id.Value);
    }

    public StructuralId Id { get; }

    public string Name { get; }

    public string SymbolKind { get; }

    public string Visibility { get; }

    public string NativeKind { get; }

    public string QualifiedName { get; }

    public string Signature { get; }

    public string AssemblyName { get; }

    public StructuralId EvidenceId { get; }

    public IReadOnlyCollection<SourceLocation> Locations => locations.Values;

    public HashSet<StructuralId> DeclaringFileIds { get; } = [];

    public HashSet<StructuralId> ContainingTypeIds { get; } = [];

    public void AddDeclaration(SourceLocation location, StructuralId sourceFileId)
    {
        locations.TryAdd(LocationKey.Create(location), location);
        DeclaringFileIds.Add(sourceFileId);
        evidenceIds.Add(EvidenceId);
    }

    public SymbolFact ToFact() => new(
        Id,
        Name,
        "csharp",
        SymbolKind,
        Visibility,
        NativeKind,
        QualifiedName,
        Signature,
        locations.Values,
        evidenceIds);

    public EvidenceFact ToEvidence() => new(
        EvidenceId,
        KnownEvidenceKinds.SourceDeclaration,
        CSharpLanguageAnalyzerId.Value,
        $"Roslyn bound {NativeKind} declaration '{QualifiedName}'.",
        ConfidenceScore.Certain,
        locations.Values);
}

internal sealed class MutableRelationshipFact
{
    private readonly Dictionary<string, SourceLocation> locations = new(StringComparer.Ordinal);
    private readonly HashSet<StructuralId> evidenceIds = [];
    private string? evidenceDescription;
    private string? evidenceKind;

    public MutableRelationshipFact(string kind, StructuralId sourceId, StructuralId targetId)
    {
        Kind = kind;
        SourceId = sourceId;
        TargetId = targetId;
        Id = StructuralId.Create("relationship", kind, sourceId.Value, targetId.Value);
    }

    public StructuralId Id { get; }

    public string Kind { get; }

    public StructuralId SourceId { get; }

    public StructuralId TargetId { get; }

    public void AddSupportingEvidence(IEnumerable<StructuralId> supportingEvidenceIds)
    {
        evidenceIds.UnionWith(supportingEvidenceIds);
    }

    public void AddSemanticEvidence(
        string kind,
        string description,
        IEnumerable<SourceLocation> evidenceLocations)
    {
        evidenceKind ??= kind;
        evidenceDescription ??= description;
        foreach (var location in evidenceLocations)
        {
            locations.TryAdd(LocationKey.Create(location), location);
        }
    }

    public RelationshipFact ToFact(ICollection<EvidenceFact> evidence)
    {
        if (evidenceDescription is not null && evidenceKind is not null)
        {
            var evidenceId = StructuralId.Create("evidence", CSharpLanguageAnalyzerId.Value, "relationship", Id.Value);
            evidence.Add(new EvidenceFact(
                evidenceId,
                evidenceKind,
                CSharpLanguageAnalyzerId.Value,
                evidenceDescription,
                ConfidenceScore.Certain,
                locations.Values));
            evidenceIds.Add(evidenceId);
        }

        return new RelationshipFact(
            Id,
            Kind,
            SourceId,
            TargetId,
            ConfidenceScore.Certain,
            evidenceIds);
    }
}

internal sealed class SymbolLookup
{
    private readonly Dictionary<string, StructuralId?> symbolIds = new(StringComparer.Ordinal);

    public void Add(MutableSymbolFact symbol)
    {
        var key = CreateKey(symbol.AssemblyName, symbol.Signature);
        if (symbolIds.TryGetValue(key, out var existing) && existing != symbol.Id)
        {
            symbolIds[key] = null;
            return;
        }

        symbolIds[key] = symbol.Id;
    }

    public bool TryResolve(ISymbol? symbol, out StructuralId id)
    {
        symbol = Normalize(symbol);
        var signature = symbol?.GetDocumentationCommentId();
        var assemblyName = symbol?.ContainingAssembly?.Name;
        if (signature is null || assemblyName is null ||
            !symbolIds.TryGetValue(CreateKey(assemblyName, signature), out var resolved) ||
            resolved is null)
        {
            id = default;
            return false;
        }

        id = resolved.Value;
        return true;
    }

    private static string CreateKey(string assemblyName, string signature) => $"{assemblyName}\u001f{signature}";

    private static ISymbol? Normalize(ISymbol? symbol)
    {
        while (symbol is IAliasSymbol alias)
        {
            symbol = alias.Target;
        }

        if (symbol is IMethodSymbol { ReducedFrom: not null } reducedMethod)
        {
            symbol = reducedMethod.ReducedFrom;
        }

        return symbol?.OriginalDefinition;
    }
}

internal static class RoslynSymbolIdentity
{
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeContainingType |
            SymbolDisplayMemberOptions.IncludeExplicitInterface |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeType,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeDefaultValue |
            SymbolDisplayParameterOptions.IncludeExtensionThis |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeType,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static StructuralId CreateId(string projectRelativePath, ISymbol symbol) => StructuralId.Create(
        "symbol",
        "csharp",
        projectRelativePath,
        GetSignature(symbol));

    public static string GetSignature(ISymbol symbol) =>
        symbol.GetDocumentationCommentId() ?? $"{symbol.Kind}:{symbol.ToDisplayString(DisplayFormat)}";

    public static string GetQualifiedName(ISymbol symbol) => symbol.ToDisplayString(DisplayFormat);

    public static string GetNativeKind(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol type => type.TypeKind.ToString().ToLowerInvariant(),
        IMethodSymbol method => $"method:{method.MethodKind.ToString().ToLowerInvariant()}",
        IPropertySymbol => "property",
        IFieldSymbol field when field.ContainingType?.TypeKind == TypeKind.Enum => "enum-member",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => symbol.Kind.ToString().ToLowerInvariant()
    };

    public static string GetVisibility(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Accessibility.Public => KnownVisibilities.Public,
        Accessibility.Protected or Accessibility.ProtectedOrInternal => KnownVisibilities.Protected,
        Accessibility.Internal or Accessibility.ProtectedAndInternal => KnownVisibilities.Internal,
        Accessibility.Private => KnownVisibilities.Private,
        _ => KnownVisibilities.Unknown
    };
}

internal static class LocationKey
{
    public static string Create(SourceLocation location) =>
        $"{location.RelativePath}\u001f{location.StartLine}\u001f{location.StartColumn}\u001f{location.EndLine}\u001f{location.EndColumn}";
}

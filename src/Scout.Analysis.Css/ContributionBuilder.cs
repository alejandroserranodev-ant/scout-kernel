using Scout.Core.Analysis;
using Scout.Core.Ir;

namespace Scout.Analysis.Css;

/// <summary>
/// Accumulates deterministic facts and merges repeated declarations of the same symbol.
/// </summary>
/// <remarks>
/// Style class names and custom properties are frequently declared by several rules and several files. The
/// graph requires one entity per identifier, so repeated declarations are merged into a single symbol carrying
/// every declaration location instead of being reported as competing symbols.
/// </remarks>
internal sealed class ContributionBuilder(string analyzerId)
{
    private readonly Dictionary<StructuralId, SourceFileFact> sourceFiles = [];
    private readonly Dictionary<StructuralId, SymbolDraft> symbols = [];
    private readonly Dictionary<StructuralId, RelationshipFact> relationships = [];
    private readonly Dictionary<StructuralId, EvidenceFact> evidence = [];

    public StructuralId AddSourceFile(string relativePath, string languageId, string? contentHash)
    {
        var id = StructuralId.Create(KnownEntityKinds.SourceFile, relativePath);
        if (sourceFiles.ContainsKey(id))
        {
            return id;
        }

        var evidenceId = AddEvidence(
            KnownEvidenceKinds.SourceDeclaration,
            $"Analyzed style sheet '{relativePath}'.",
            location: null,
            identity: ["source-file", relativePath]);
        sourceFiles[id] = new SourceFileFact(
            id,
            relativePath,
            languageId,
            contentHash,
            string.IsNullOrEmpty(contentHash) ? null : "sha-256",
            [evidenceId]);
        return id;
    }

    public StructuralId AddSymbol(
        StructuralId id,
        string name,
        string languageId,
        string symbolKind,
        string visibility,
        string nativeKind,
        string qualifiedName,
        SourceLocation location,
        string evidenceDescription)
    {
        var evidenceId = AddEvidence(
            KnownEvidenceKinds.SourceDeclaration,
            evidenceDescription,
            location,
            ["symbol", qualifiedName, location.RelativePath, location.StartLine.ToString(Culture)]);

        if (symbols.TryGetValue(id, out var existing))
        {
            existing.Locations.Add(location);
            existing.EvidenceIds.Add(evidenceId);
            return id;
        }

        symbols[id] = new SymbolDraft(
            id,
            name,
            languageId,
            symbolKind,
            visibility,
            nativeKind,
            qualifiedName,
            [location],
            [evidenceId]);
        return id;
    }

    public void AddRelationship(
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        SourceLocation location)
    {
        var id = StructuralId.Create("relationship", kind, sourceId.Value, targetId.Value);
        if (relationships.ContainsKey(id))
        {
            return;
        }

        var evidenceId = AddEvidence(
            KnownEvidenceKinds.SourceDeclaration,
            $"Observed '{kind}' at {location.RelativePath}:{location.StartLine}.",
            location,
            ["relationship", kind, sourceId.Value, targetId.Value]);
        relationships[id] = new RelationshipFact(
            id,
            kind,
            sourceId,
            targetId,
            ConfidenceScore.Certain,
            [evidenceId]);
    }

    public LanguageAnalysisResult Build(
        AnalyzerRunMetadata metadata,
        IEnumerable<AnalysisDiagnostic> diagnostics)
    {
        return new LanguageAnalysisResult(
            metadata,
            projects: null,
            sourceFiles: sourceFiles.Values,
            symbols: symbols.Values.Select(draft => draft.ToFact()),
            relationships: relationships.Values,
            evidence: evidence.Values,
            diagnostics: diagnostics);
    }

    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;

    private StructuralId AddEvidence(
        string kind,
        string description,
        SourceLocation? location,
        string[] identity)
    {
        var id = StructuralId.Create("evidence", [analyzerId, .. identity]);
        if (!evidence.ContainsKey(id))
        {
            evidence[id] = new EvidenceFact(
                id,
                kind,
                analyzerId,
                description,
                ConfidenceScore.Certain,
                location is null ? null : [location]);
        }

        return id;
    }

    private sealed record SymbolDraft(
        StructuralId Id,
        string Name,
        string LanguageId,
        string SymbolKind,
        string Visibility,
        string NativeKind,
        string QualifiedName,
        List<SourceLocation> Locations,
        List<StructuralId> EvidenceIds)
    {
        public SymbolFact ToFact()
        {
            return new SymbolFact(
                Id,
                Name,
                LanguageId,
                SymbolKind,
                visibility: Visibility,
                nativeKind: NativeKind,
                qualifiedName: QualifiedName,
                declarationLocations: Locations,
                evidenceIds: EvidenceIds);
        }
    }
}

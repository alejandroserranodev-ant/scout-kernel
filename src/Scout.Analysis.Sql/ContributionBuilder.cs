using Scout.Core.Analysis;
using Scout.Core.Ir;

namespace Scout.Analysis.Sql;

/// <summary>
/// Accumulates deterministic facts for the SQL files of one repository.
/// </summary>
/// <remarks>
/// Every declaration is addressed by the file it appears in, so the same table named in two migrations is two
/// symbols, one per place it is declared.
/// </remarks>
internal sealed class ContributionBuilder(string analyzerId)
{
    private readonly Dictionary<StructuralId, SourceFileFact> sourceFiles = [];
    private readonly Dictionary<StructuralId, SymbolFact> symbols = [];
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
            $"Analyzed SQL file '{relativePath}'.",
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
        string nativeKind,
        string qualifiedName,
        SourceLocation location,
        string evidenceDescription)
    {
        var evidenceId = AddEvidence(
            KnownEvidenceKinds.SourceDeclaration,
            evidenceDescription,
            location,
            ["symbol", qualifiedName, location.RelativePath]);

        symbols[id] = new SymbolFact(
            id,
            name,
            languageId,
            symbolKind,
            visibility: KnownVisibilities.Unknown,
            nativeKind: nativeKind,
            qualifiedName: qualifiedName,
            declarationLocations: [location],
            evidenceIds: [evidenceId]);
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
            symbols: symbols.Values,
            relationships: relationships.Values,
            evidence: evidence.Values,
            diagnostics: diagnostics);
    }

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
}

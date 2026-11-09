using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.TypeScript;

/// <summary>
/// Projects a sidecar payload onto language-neutral structural facts.
/// </summary>
/// <remarks>
/// The mapper only translates. It never repairs the payload: a relationship whose endpoint was not declared is
/// dropped rather than materialized, because the graph treats every relationship as an observed fact.
/// </remarks>
internal sealed class SidecarFactMapper
{
    private const string PositionEncoding = "utf-16-code-units";

    private readonly RepositoryDescriptor repository;
    private readonly string analyzerId;
    private readonly Dictionary<string, StructuralId> projectIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructuralId> sourceFileIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructuralId> symbolIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StructuralId> entityEvidenceIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SidecarLocation> symbolLocations = new(StringComparer.Ordinal);
    private readonly List<EvidenceFact> evidence = [];

    internal SidecarFactMapper(RepositoryDescriptor repository, string analyzerId)
    {
        this.repository = repository;
        this.analyzerId = analyzerId;
    }

    internal LanguageAnalysisResult Map(SidecarResponse response, AnalyzerRunMetadata analyzer)
    {
        var projects = response.Projects.Select(MapProject).ToArray();
        var sourceFiles = response.SourceFiles.Select(MapSourceFile).ToArray();
        var symbols = response.Symbols.Select(MapSymbol).ToArray();
        var relationships = response.Relationships
            .Select(MapRelationship)
            .OfType<RelationshipFact>()
            .ToArray();
        var diagnostics = response.Diagnostics.Select(MapDiagnostic).ToArray();

        return new LanguageAnalysisResult(
            analyzer,
            projects,
            sourceFiles,
            symbols,
            relationships,
            evidence,
            diagnostics);
    }

    private ProjectFact MapProject(SidecarProject project)
    {
        var id = StructuralId.Create("project", project.RelativePath);
        projectIds[project.RelativePath] = id;
        var evidenceId = AddEvidence(
            $"project|{project.RelativePath}",
            KnownEvidenceKinds.ProjectMetadata,
            $"Observed project manifest '{project.RelativePath}'.",
            locations: []);
        return new ProjectFact(id, project.Name, project.LanguageId, project.RelativePath, [evidenceId]);
    }

    private SourceFileFact MapSourceFile(SidecarSourceFile sourceFile)
    {
        var id = StructuralId.Create("source-file", sourceFile.RelativePath);
        sourceFileIds[sourceFile.RelativePath] = id;
        var discovered = repository.Files
            .FirstOrDefault(file => string.Equals(file.RelativePath, sourceFile.RelativePath, StringComparison.Ordinal));
        var evidenceId = AddEvidence(
            $"source-file|{sourceFile.RelativePath}",
            KnownEvidenceKinds.SourceDeclaration,
            $"Analyzed source file '{sourceFile.RelativePath}'.",
            locations: []);

        return new SourceFileFact(
            id,
            sourceFile.RelativePath,
            sourceFile.LanguageId,
            discovered?.ContentHash,
            discovered is null ? null : "sha-256",
            [evidenceId]);
    }

    private SymbolFact MapSymbol(SidecarSymbol symbol)
    {
        var id = StructuralId.Create("symbol", symbol.Key);
        symbolIds[symbol.Key] = id;
        symbolLocations[symbol.Key] = symbol.Location;
        var location = ToSourceLocation(symbol.Location);
        // Wording is observed in a declaration, not declared by it, so its evidence says what was seen.
        var description = string.Equals(symbol.SymbolKind, KnownSymbolKinds.TextLiteral, StringComparison.Ordinal)
            ? $"Observed {symbol.NativeKind} text '{symbol.Name}'."
            : $"Declared {symbol.NativeKind} '{symbol.Name}'.";
        var evidenceId = AddEvidence(
            $"symbol|{symbol.Key}",
            KnownEvidenceKinds.SourceDeclaration,
            description,
            [location]);

        return new SymbolFact(
            id,
            symbol.Name,
            symbol.LanguageId,
            symbol.SymbolKind,
            visibility: symbol.Visibility,
            nativeKind: symbol.NativeKind,
            qualifiedName: symbol.QualifiedName,
            declarationLocations: [location],
            evidenceIds: [evidenceId]);
    }

    private RelationshipFact? MapRelationship(SidecarRelationship relationship)
    {
        if (!TryResolveEntity(relationship.SourceKind, relationship.SourceKey, out var sourceId) ||
            !TryResolveEntity(relationship.TargetKind, relationship.TargetKey, out var targetId))
        {
            return null;
        }

        StructuralId evidenceId;
        if (IsSemanticRelationship(relationship.Kind))
        {
            evidenceId = AddEvidence(
                $"relationship|{relationship.Kind}|{relationship.SourceKey}|{relationship.TargetKey}",
                KnownEvidenceKinds.SemanticBinding,
                $"The TypeScript checker resolved '{relationship.SourceKey}' to {relationship.Kind} '{relationship.TargetKey}'.",
                ResolveRelationshipLocations(relationship));
        }
        else if (!entityEvidenceIds.TryGetValue(
            $"{relationship.SourceKind}|{relationship.SourceKey}",
            out evidenceId))
        {
            return null;
        }

        return new RelationshipFact(
            StructuralId.Create("relationship", sourceId.Value, relationship.Kind, targetId.Value),
            relationship.Kind,
            sourceId,
            targetId,
            ConfidenceScore.Certain,
            [evidenceId]);
    }

    private static bool IsSemanticRelationship(string kind)
    {
        return kind is
            KnownRelationshipKinds.Calls or
            KnownRelationshipKinds.Implements or
            KnownRelationshipKinds.Inherits or
            KnownRelationshipKinds.References or
            KnownRelationshipKinds.Renders;
    }

    private IReadOnlyList<SourceLocation> ResolveRelationshipLocations(SidecarRelationship relationship)
    {
        return string.Equals(relationship.SourceKind, "symbol", StringComparison.Ordinal) &&
               symbolLocations.TryGetValue(relationship.SourceKey, out var location)
            ? [ToSourceLocation(location)]
            : [];
    }

    private bool TryResolveEntity(string entityKind, string key, out StructuralId id)
    {
        if (string.Equals(entityKind, "symbol", StringComparison.Ordinal) && SharedSymbolKey.IsSharedKey(key))
        {
            // The declaration belongs to another analyzer. Both sides derive the identifier from the same
            // logical key; the compiler drops the reference with a diagnostic when that analyzer did not run.
            id = StructuralId.Create(KnownEntityKinds.Symbol, key);
            return true;
        }

        var lookup = entityKind switch
        {
            "project" => projectIds,
            "source-file" => sourceFileIds,
            "symbol" => symbolIds,
            _ => null
        };

        if (lookup is null)
        {
            id = default;
            return false;
        }

        return lookup.TryGetValue(key, out id);
    }

    private AnalysisDiagnostic MapDiagnostic(SidecarDiagnostic diagnostic)
    {
        return new AnalysisDiagnostic(
            StructuralId.Create("diagnostic", analyzerId, diagnostic.Code, diagnostic.Message),
            analyzerId,
            diagnostic.Severity,
            diagnostic.Code,
            diagnostic.Message);
    }

    private StructuralId AddEvidence(
        string identity,
        string kind,
        string description,
        IReadOnlyList<SourceLocation> locations)
    {
        if (entityEvidenceIds.TryGetValue(identity, out var existing))
        {
            return existing;
        }

        var id = StructuralId.Create("evidence", analyzerId, identity);
        entityEvidenceIds[identity] = id;
        evidence.Add(new EvidenceFact(id, kind, analyzerId, description, ConfidenceScore.Certain, locations));
        return id;
    }

    private static SourceLocation ToSourceLocation(SidecarLocation location)
    {
        return new SourceLocation(
            location.RelativePath,
            location.StartLine,
            location.StartColumn,
            location.EndLine,
            location.EndColumn,
            PositionEncoding,
            StructuralId.Create("source-file", location.RelativePath));
    }
}

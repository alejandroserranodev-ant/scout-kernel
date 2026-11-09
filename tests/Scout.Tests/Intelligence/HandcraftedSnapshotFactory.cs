using Scout.Core.Ir;

namespace Scout.Tests.Intelligence;

internal static class HandcraftedSnapshotFactory
{
    internal const string HandlerDirectory = "src/Billing/Application/Handlers";

    internal static DateTimeOffset CreatedAtUtc { get; } =
        new(2026, 8, 26, 4, 30, 0, TimeSpan.Zero);

    internal static StructuralSnapshot CreateConventionSnapshot(params string[] typeNames)
    {
        return CreateSnapshot(typeNames, includeProject: false);
    }

    internal static StructuralSnapshot CreateKernelSnapshot()
    {
        return CreateSnapshot(
            [
                "CancelInvoiceHandler",
                "CreateInvoiceHandler",
                "GetInvoiceHandler",
                "UpdateInvoiceHandler"
            ],
            includeProject: true);
    }

    /// <summary>
    /// Creates a snapshot where handlers depend on a shared abstraction so that contract compilation has
    /// observed consumers to reason about.
    /// </summary>
    internal static StructuralSnapshot CreateContractSnapshot()
    {
        var baseSnapshot = CreateKernelSnapshot();
        const string abstractionPath = "src/Billing/Application/IInvoiceStore.cs";
        const string implementationPath = "src/Billing/Infrastructure/InMemoryInvoiceStore.cs";

        var projectId = StructuralId.Create("project", "src/Billing/Billing.csproj");
        var projectEvidenceId = StructuralId.Create("evidence", "src/Billing/Billing.csproj");
        var sourceFiles = baseSnapshot.SourceFiles.ToList();
        var symbols = baseSnapshot.Symbols.ToList();
        var relationships = baseSnapshot.Relationships.ToList();
        var evidence = baseSnapshot.Evidence.ToList();

        var abstractionId = AddType(
            "IInvoiceStore",
            abstractionPath,
            "interface",
            sourceFiles,
            symbols,
            relationships,
            evidence,
            projectId,
            projectEvidenceId);
        var implementationId = AddType(
            "InMemoryInvoiceStore",
            implementationPath,
            "class",
            sourceFiles,
            symbols,
            relationships,
            evidence,
            projectId,
            projectEvidenceId);

        relationships.Add(CreateRelationship(
            KnownRelationshipKinds.Implements,
            implementationId,
            abstractionId,
            projectEvidenceId));
        foreach (var handler in symbols.Where(symbol => symbol.Name.EndsWith("Handler", StringComparison.Ordinal)))
        {
            relationships.Add(CreateRelationship(
                KnownRelationshipKinds.References,
                handler.Id,
                abstractionId,
                projectEvidenceId));
        }

        return new StructuralSnapshot(
            baseSnapshot.Source,
            CreatedAtUtc,
            baseSnapshot.Analyzers,
            baseSnapshot.Projects,
            sourceFiles,
            symbols,
            relationships,
            evidence);
    }

    private static StructuralId AddType(
        string typeName,
        string relativePath,
        string nativeKind,
        List<SourceFileFact> sourceFiles,
        List<SymbolFact> symbols,
        List<RelationshipFact> relationships,
        List<EvidenceFact> evidence,
        StructuralId projectId,
        StructuralId projectEvidenceId)
    {
        var sourceFileId = StructuralId.Create("source-file", relativePath);
        var symbolId = StructuralId.Create("symbol", $"Billing.{typeName}");
        var evidenceId = StructuralId.Create("evidence", relativePath, typeName);
        var location = new SourceLocation(
            relativePath,
            startLine: 1,
            startColumn: 1,
            endLine: 1,
            endColumn: typeName.Length + 1,
            positionEncoding: "utf-16-code-units",
            sourceFileId);

        evidence.Add(new EvidenceFact(
            evidenceId,
            KnownEvidenceKinds.SourceDeclaration,
            "handcrafted-tests",
            $"Declared type {typeName}.",
            ConfidenceScore.Certain,
            [location]));
        sourceFiles.Add(new SourceFileFact(sourceFileId, relativePath, "csharp", evidenceIds: [evidenceId]));
        symbols.Add(new SymbolFact(
            symbolId,
            typeName,
            "csharp",
            KnownSymbolKinds.Type,
            visibility: KnownVisibilities.Public,
            nativeKind: nativeKind,
            qualifiedName: $"Billing.{typeName}",
            declarationLocations: [location],
            evidenceIds: [evidenceId]));
        relationships.Add(CreateRelationship(
            KnownRelationshipKinds.Defines,
            sourceFileId,
            symbolId,
            evidenceId));
        relationships.Add(CreateRelationship(
            KnownRelationshipKinds.Contains,
            projectId,
            sourceFileId,
            projectEvidenceId));
        return symbolId;
    }

    private static RelationshipFact CreateRelationship(
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        StructuralId evidenceId)
    {
        return new RelationshipFact(
            StructuralId.Create("relationship", sourceId.Value, kind, targetId.Value),
            kind,
            sourceId,
            targetId,
            ConfidenceScore.Certain,
            [evidenceId]);
    }

    private static StructuralSnapshot CreateSnapshot(
        IEnumerable<string> typeNames,
        bool includeProject)
    {
        var sourceFiles = new List<SourceFileFact>();
        var symbols = new List<SymbolFact>();
        var relationships = new List<RelationshipFact>();
        var evidence = new List<EvidenceFact>();

        foreach (var typeName in typeNames)
        {
            var relativePath = $"{HandlerDirectory}/{typeName}.cs";
            var sourceFileId = StructuralId.Create("source-file", relativePath);
            var symbolId = StructuralId.Create("symbol", $"Billing.Application.Handlers.{typeName}");
            var evidenceId = StructuralId.Create("evidence", relativePath, typeName);
            var location = new SourceLocation(
                relativePath,
                startLine: 1,
                startColumn: 1,
                endLine: 1,
                endColumn: typeName.Length + 1,
                positionEncoding: "utf-16-code-units",
                sourceFileId);

            evidence.Add(new EvidenceFact(
                evidenceId,
                KnownEvidenceKinds.SourceDeclaration,
                "handcrafted-tests",
                $"Declared type {typeName}.",
                ConfidenceScore.Certain,
                [location]));
            sourceFiles.Add(new SourceFileFact(
                sourceFileId,
                relativePath,
                "csharp",
                evidenceIds: [evidenceId]));
            symbols.Add(new SymbolFact(
                symbolId,
                typeName,
                "csharp",
                KnownSymbolKinds.Type,
                nativeKind: "class",
                qualifiedName: $"Billing.Application.Handlers.{typeName}",
                declarationLocations: [location],
                evidenceIds: [evidenceId]));
            relationships.Add(new RelationshipFact(
                StructuralId.Create("relationship", sourceFileId.Value, KnownRelationshipKinds.Defines, symbolId.Value),
                KnownRelationshipKinds.Defines,
                sourceFileId,
                symbolId,
                ConfidenceScore.Certain,
                [evidenceId]));
        }

        ProjectFact[] projects = [];
        if (includeProject)
        {
            const string projectPath = "src/Billing/Billing.csproj";
            var projectId = StructuralId.Create("project", projectPath);
            var projectEvidenceId = StructuralId.Create("evidence", projectPath);
            projects =
            [
                new ProjectFact(projectId, "Billing", "csharp", projectPath, [projectEvidenceId])
            ];
            evidence.Add(new EvidenceFact(
                projectEvidenceId,
                KnownEvidenceKinds.ProjectMetadata,
                "handcrafted-tests",
                "Observed the Billing source project.",
                ConfidenceScore.Certain,
                [new SourceLocation(projectPath, 1, 1, 1, 2, "utf-16-code-units")]));

            relationships.AddRange(sourceFiles.Select(sourceFile => new RelationshipFact(
                StructuralId.Create(
                    "relationship",
                    projectId.Value,
                    KnownRelationshipKinds.Contains,
                    sourceFile.Id.Value),
                KnownRelationshipKinds.Contains,
                projectId,
                sourceFile.Id,
                ConfidenceScore.Certain,
                [projectEvidenceId])));
        }

        var sourceIdentity = new SourceIdentity(
            StructuralId.Create("repository", "MinimalCSharpRepository"),
            "MinimalCSharpRepository",
            KnownRevisionKinds.GitCommit,
            revision: "0123456789abcdef",
            contentFingerprint: new string('f', 64));
        var analyzer = new AnalyzerRunMetadata(
            "handcrafted-tests",
            "Handcrafted test analyzer",
            "1.0.0",
            "csharp",
            KnownAnalyzerStatuses.Succeeded,
            CreatedAtUtc,
            "utf-16-code-units");

        return new StructuralSnapshot(
            sourceIdentity,
            CreatedAtUtc,
            analyzers: [analyzer],
            projects,
            sourceFiles,
            symbols,
            relationships,
            evidence);
    }
}

using Scout.Core.Graph;
using Scout.Core.Ir;

namespace Scout.Tests.Graph;

public sealed class ProjectGraphTests
{
    [Fact]
    public void ConstructorRejectsRelationshipWithMissingEndpoint()
    {
        var project = CreateProject("Billing");
        var missingSymbolId = StructuralId.Create("symbol", "src/Billing/Billing.csproj", "Billing.Invoice");
        var relationship = CreateRelationship(
            "missing-target",
            KnownRelationshipKinds.Defines,
            project.Id,
            missingSymbolId);

        var exception = Assert.Throws<ProjectGraphValidationException>(
            () => new ProjectGraph(CreateSnapshot(projects: [project], relationships: [relationship])));

        Assert.Contains("missing target entity", exception.Message);
        Assert.Contains(missingSymbolId.Value, exception.Message);
    }

    [Fact]
    public void ConstructorRejectsDuplicateEntityIdentifierAcrossEntityKinds()
    {
        var duplicateId = StructuralId.Create("entity", "shared-identity");
        var project = new ProjectFact(duplicateId, "Billing", "csharp", "src/Billing/Billing.csproj");
        var symbol = new SymbolFact(
            duplicateId,
            "Invoice",
            "csharp",
            KnownSymbolKinds.Type,
            nativeKind: "class",
            qualifiedName: "Billing.Invoice");

        var exception = Assert.Throws<ProjectGraphValidationException>(
            () => new ProjectGraph(CreateSnapshot(projects: [project], symbols: [symbol])));

        Assert.Contains("used by more than one entity", exception.Message);
        Assert.Contains(duplicateId.Value, exception.Message);
    }

    [Fact]
    public void ConstructorMergesDuplicateEdgesAndPreservesTheirProvenance()
    {
        var project = CreateProject("Billing");
        var symbol = CreateSymbol("Billing.Invoice");
        var firstEvidence = CreateEvidence("first");
        var sharedEvidence = CreateEvidence("shared");
        var strongestEvidence = CreateEvidence("strongest");
        var firstRelationship = CreateRelationship(
            "first-observation",
            KnownRelationshipKinds.Defines,
            project.Id,
            symbol.Id,
            0.62,
            [sharedEvidence.Id, firstEvidence.Id]);
        var secondRelationship = CreateRelationship(
            "second-observation",
            KnownRelationshipKinds.Defines,
            project.Id,
            symbol.Id,
            0.91,
            [strongestEvidence.Id, sharedEvidence.Id]);

        var graph = new ProjectGraph(CreateSnapshot(
            projects: [project],
            symbols: [symbol],
            relationships: [secondRelationship, firstRelationship],
            evidence: [strongestEvidence, firstEvidence, sharedEvidence]));

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(0.91, edge.Confidence.Value);
        Assert.Equal(
            new[] { firstRelationship.Id, secondRelationship.Id }.Order(),
            edge.RelationshipIds);
        Assert.Equal(
            new[] { firstEvidence.Id, sharedEvidence.Id, strongestEvidence.Id }.Order(),
            edge.EvidenceIds);
    }

    [Fact]
    public void ConstructorRejectsRelationshipIdentifierUsedForConflictingEdges()
    {
        var project = CreateProject("Billing");
        var invoice = CreateSymbol("Billing.Invoice");
        var customer = CreateSymbol("Billing.Customer");
        var conflictingId = StructuralId.Create("relationship", "conflicting-observation");
        var firstRelationship = new RelationshipFact(
            conflictingId,
            KnownRelationshipKinds.Defines,
            project.Id,
            invoice.Id,
            ConfidenceScore.Certain);
        var secondRelationship = new RelationshipFact(
            conflictingId,
            KnownRelationshipKinds.Defines,
            project.Id,
            customer.Id,
            ConfidenceScore.Certain);

        var exception = Assert.Throws<ProjectGraphValidationException>(() => new ProjectGraph(CreateSnapshot(
            projects: [project],
            symbols: [invoice, customer],
            relationships: [secondRelationship, firstRelationship])));

        Assert.Contains("describes more than one source-kind-target tuple", exception.Message);
        Assert.Contains(conflictingId.Value, exception.Message);
    }

    [Fact]
    public void FilteredTraversalIsIndexedAndDeterministicallyOrdered()
    {
        var project = CreateProject("Billing");
        var sourceFile = new SourceFileFact(
            StructuralId.Create("source-file", "src/Billing/Invoice.cs"),
            "src/Billing/Invoice.cs",
            "csharp");
        var invoice = CreateSymbol("Billing.Invoice");
        var customer = CreateSymbol("Billing.Customer");
        var payment = CreateSymbol("Billing.Payment");
        var relationships = new[]
        {
            CreateRelationship("payment-references-invoice", KnownRelationshipKinds.References, payment.Id, invoice.Id),
            CreateRelationship("project-defines-invoice", KnownRelationshipKinds.Defines, project.Id, invoice.Id),
            CreateRelationship("customer-references-invoice", KnownRelationshipKinds.References, customer.Id, invoice.Id),
            CreateRelationship("project-contains-file", KnownRelationshipKinds.Contains, project.Id, sourceFile.Id),
            CreateRelationship("project-defines-customer", KnownRelationshipKinds.Defines, project.Id, customer.Id),
        };

        var graph = new ProjectGraph(CreateSnapshot(
            projects: [project],
            sourceFiles: [sourceFile],
            symbols: [payment, invoice, customer],
            relationships: relationships.Reverse()));

        var definedTargets = graph
            .GetOutgoingEdges(project.Id, KnownRelationshipKinds.Defines.ToUpperInvariant())
            .Select(edge => edge.TargetId)
            .ToArray();
        var expectedDefinedTargets = new[] { invoice.Id, customer.Id }.Order().ToArray();
        Assert.Equal(expectedDefinedTargets, definedTargets);

        var referenceSources = graph
            .GetIncomingEdges(invoice.Id, KnownRelationshipKinds.References)
            .Select(edge => edge.SourceId)
            .ToArray();
        var expectedReferenceSources = new[] { payment.Id, customer.Id }.Order().ToArray();
        Assert.Equal(expectedReferenceSources, referenceSources);

        var recreatedGraph = new ProjectGraph(CreateSnapshot(
            projects: [project],
            sourceFiles: [sourceFile],
            symbols: [customer, invoice, payment],
            relationships: relationships));
        Assert.Equal(
            graph.Edges.Select(edge => edge.Id),
            recreatedGraph.Edges.Select(edge => edge.Id));
    }

    private static ProjectFact CreateProject(string name)
    {
        var relativePath = $"src/{name}/{name}.csproj";
        return new ProjectFact(
            StructuralId.Create("project", "csharp", relativePath),
            name,
            "csharp",
            relativePath);
    }

    private static SymbolFact CreateSymbol(string qualifiedName) =>
        new(
            StructuralId.Create("symbol", "src/Billing/Billing.csproj", qualifiedName),
            qualifiedName[(qualifiedName.LastIndexOf('.') + 1)..],
            "csharp",
            KnownSymbolKinds.Type,
            nativeKind: "class",
            qualifiedName: qualifiedName);

    private static EvidenceFact CreateEvidence(string identity) =>
        new(
            StructuralId.Create("evidence", identity),
            KnownEvidenceKinds.SemanticBinding,
            "scout.csharp",
            $"Semantic evidence for {identity}.",
            ConfidenceScore.Certain);

    private static RelationshipFact CreateRelationship(
        string identity,
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        double confidence = 1,
        IEnumerable<StructuralId>? evidenceIds = null) =>
        new(
            StructuralId.Create("relationship", identity),
            kind,
            sourceId,
            targetId,
            new ConfidenceScore(confidence),
            evidenceIds);

    private static StructuralSnapshot CreateSnapshot(
        IEnumerable<ProjectFact>? projects = null,
        IEnumerable<SourceFileFact>? sourceFiles = null,
        IEnumerable<SymbolFact>? symbols = null,
        IEnumerable<RelationshipFact>? relationships = null,
        IEnumerable<EvidenceFact>? evidence = null) =>
        new(
            new SourceIdentity(
                StructuralId.Create("repository", "graph-tests"),
                "Graph tests",
                KnownRevisionKinds.Unknown),
            DateTimeOffset.UnixEpoch,
            projects: projects,
            sourceFiles: sourceFiles,
            symbols: symbols,
            relationships: relationships,
            evidence: evidence);
}

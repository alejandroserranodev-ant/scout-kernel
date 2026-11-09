using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using RepositoryPaths = Scout.Core.Repository.RepositoryPath;

namespace Scout.Analysis.CSharp;

internal sealed class CSharpFactExtractor
{
    private readonly RepositoryDescriptor repository;
    private readonly string analyzerVersion;
    private readonly string positionEncoding;
    private readonly MsBuildRegistrationResult registration;
    private readonly DiagnosticAccumulator diagnostics;
    private readonly RepositoryFileIndex fileIndex;
    private readonly Dictionary<string, string> projectPaths;
    private readonly Dictionary<StructuralId, ProjectFact> projects = [];
    private readonly Dictionary<StructuralId, SourceFileFact> sourceFiles = [];
    private readonly Dictionary<StructuralId, MutableSymbolFact> symbols = [];
    private readonly Dictionary<StructuralId, EvidenceFact> evidence = [];
    private readonly Dictionary<StructuralId, MutableRelationshipFact> relationships = [];
    private readonly List<DeclaredSymbolObservation> declaredSymbols = [];
    private readonly List<ProjectContext> projectContexts = [];
    private int successfulCompilationCount;

    public CSharpFactExtractor(
        RepositoryDescriptor repository,
        string analyzerVersion,
        string positionEncoding,
        MsBuildRegistrationResult registration)
    {
        this.repository = repository;
        this.analyzerVersion = analyzerVersion;
        this.positionEncoding = positionEncoding;
        this.registration = registration;
        diagnostics = new DiagnosticAccumulator(repository.RootPath);
        fileIndex = new RepositoryFileIndex(repository);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        projectPaths = repository.FindByExtension(".csproj")
            .ToDictionary(static file => file.RelativePath, static file => file.RelativePath, pathComparer);
    }

    public async Task<LanguageAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        if (registration.Warning is not null)
        {
            diagnostics.Add(
                "CSHARP_MSBUILD_REGISTRATION_UNVERIFIED",
                KnownDiagnosticSeverities.Warning,
                registration.Warning,
                affectsCompleteness: true);
        }

        var workspaceDiagnostics = new ConcurrentQueue<WorkspaceDiagnostic>();
        using var workspace = MSBuildWorkspace.Create();
        workspace.SkipUnrecognizedProjects = true;
        using var workspaceFailureRegistration = workspace.RegisterWorkspaceFailedHandler(
            eventArgs => workspaceDiagnostics.Enqueue(eventArgs.Diagnostic));

        Solution? solution;
        try
        {
            solution = await LoadSolutionAsync(workspace, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            DrainWorkspaceDiagnostics(workspaceDiagnostics);
            diagnostics.Add(
                "CSHARP_PROJECT_LOAD_FAILURE",
                KnownDiagnosticSeverities.Error,
                exception.Message,
                affectsCompleteness: true);
            return BuildResult(KnownAnalyzerStatuses.Failed);
        }

        DrainWorkspaceDiagnostics(workspaceDiagnostics);
        if (solution is null)
        {
            return BuildResult(KnownAnalyzerStatuses.Failed);
        }

        await CreateProjectContextsAsync(solution, cancellationToken).ConfigureAwait(false);
        CreateProjectDependencies();
        ExtractDeclarations(cancellationToken);
        ExtractStructuralRelationships();
        ExtractSemanticRelationships(cancellationToken);
        DrainWorkspaceDiagnostics(workspaceDiagnostics);

        var status = successfulCompilationCount == 0
            ? KnownAnalyzerStatuses.Failed
            : diagnostics.HasCompletenessFailure
                ? KnownAnalyzerStatuses.Partial
                : KnownAnalyzerStatuses.Succeeded;
        return BuildResult(status);
    }

    private async Task<Solution?> LoadSolutionAsync(
        MSBuildWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var solutionFiles = repository.Files
            .Where(static file =>
                string.Equals(Path.GetExtension(file.RelativePath), ".sln", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(file.RelativePath), ".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var projectFiles = repository.FindByExtension(".csproj");

        if (solutionFiles.Length == 1)
        {
            var solutionPath = RepositoryPaths.Resolve(repository.RootPath, solutionFiles[0].RelativePath);
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        if (solutionFiles.Length > 1)
        {
            diagnostics.Add(
                "CSHARP_MULTIPLE_SOLUTIONS",
                KnownDiagnosticSeverities.Warning,
                "Multiple solution files were discovered. Scout analyzed discovered C# project files directly.",
                affectsCompleteness: true);
        }

        if (projectFiles.Count == 0)
        {
            diagnostics.Add(
                "CSHARP_NO_PROJECTS",
                KnownDiagnosticSeverities.Error,
                "No C# solution or project file was discovered.",
                affectsCompleteness: true);
            return null;
        }

        foreach (var projectFile in projectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = RepositoryPaths.Resolve(repository.RootPath, projectFile.RelativePath);
            try
            {
                _ = await workspace.OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(
                    "CSHARP_PROJECT_LOAD_FAILURE",
                    KnownDiagnosticSeverities.Error,
                    $"Project '{projectFile.RelativePath}' could not be loaded: {exception.Message}",
                    affectsCompleteness: true);
            }
        }

        return workspace.CurrentSolution;
    }

    private async Task CreateProjectContextsAsync(Solution solution, CancellationToken cancellationToken)
    {
        var candidates = new List<(Project Project, string RelativePath)>();
        foreach (var project in solution.Projects.Where(static project => project.Language == LanguageNames.CSharp))
        {
            if (!TryGetProjectRelativePath(project.FilePath, out var relativePath))
            {
                diagnostics.Add(
                    "CSHARP_PROJECT_OUTSIDE_REPOSITORY",
                    KnownDiagnosticSeverities.Warning,
                    $"C# project '{project.Name}' is outside the discovered repository and was skipped.",
                    affectsCompleteness: true);
                continue;
            }

            candidates.Add((project, relativePath));
        }

        foreach (var candidate in candidates
                     .OrderBy(static candidate => candidate.RelativePath, StringComparer.Ordinal)
                     .ThenBy(static candidate => candidate.Project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectId = StructuralId.Create("project", "csharp", candidate.RelativePath);
            EnsureProjectFact(projectId, candidate.RelativePath);

            Compilation? compilation;
            try
            {
                compilation = await candidate.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(
                    "CSHARP_COMPILATION_FAILURE",
                    KnownDiagnosticSeverities.Error,
                    $"Compilation for '{candidate.RelativePath}' failed: {exception.Message}",
                    affectsCompleteness: true);
                continue;
            }

            if (compilation is null)
            {
                diagnostics.Add(
                    "CSHARP_COMPILATION_UNAVAILABLE",
                    KnownDiagnosticSeverities.Error,
                    $"Compilation for '{candidate.RelativePath}' was unavailable.",
                    affectsCompleteness: true);
                continue;
            }

            successfulCompilationCount++;
            CaptureCompilationDiagnostics(compilation, candidate.Project.FilePath, cancellationToken);
            ValidateCompilation(compilation, candidate.Project, candidate.RelativePath);

            var context = new ProjectContext(
                candidate.Project,
                candidate.RelativePath,
                projectId,
                compilation.Assembly.Name);
            await AddDocumentsAsync(context, cancellationToken).ConfigureAwait(false);
            projectContexts.Add(context);
        }

        if (candidates.Count == 0)
        {
            diagnostics.Add(
                "CSHARP_NO_LOADABLE_PROJECTS",
                KnownDiagnosticSeverities.Error,
                "No loadable C# projects were found in the selected inputs.",
                affectsCompleteness: true);
        }
    }

    private async Task AddDocumentsAsync(ProjectContext context, CancellationToken cancellationToken)
    {
        var projectDirectory = Path.GetDirectoryName(context.Project.FilePath);
        foreach (var document in context.Project.Documents
                     .OrderBy(static document => document.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!fileIndex.TryGetFile(document.FilePath, projectDirectory, out var indexedFile))
            {
                continue;
            }

            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (root is null || semanticModel is null)
            {
                diagnostics.Add(
                    "CSHARP_DOCUMENT_MODEL_UNAVAILABLE",
                    KnownDiagnosticSeverities.Error,
                    $"Roslyn could not create a semantic model for '{indexedFile.RelativePath}'.",
                    affectsCompleteness: true);
                continue;
            }

            EnsureSourceFileFact(indexedFile);
            AddStructuralRelationship(
                KnownRelationshipKinds.Contains,
                context.ProjectId,
                indexedFile.Id,
                [GetProjectEvidenceId(context.ProjectId), GetSourceFileEvidenceId(indexedFile.Id)]);
            context.Documents.Add(new DocumentContext(root, semanticModel, indexedFile));
        }
    }

    private void CaptureCompilationDiagnostics(
        Compilation compilation,
        string? projectFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
            {
                diagnostics.AddCompilerDiagnostic(
                    diagnostic,
                    location => fileIndex.TryCreateLocation(location, Path.GetDirectoryName(projectFilePath)));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Add(
                "CSHARP_DIAGNOSTIC_COLLECTION_FAILURE",
                KnownDiagnosticSeverities.Warning,
                exception.Message,
                affectsCompleteness: true);
        }
    }

    private void ValidateCompilation(Compilation compilation, Project project, string relativePath)
    {
        if (project.MetadataReferences.Count == 0 ||
            compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
        {
            diagnostics.Add(
                "CSHARP_INVALID_SEMANTIC_MODEL",
                KnownDiagnosticSeverities.Error,
                $"Compilation for '{relativePath}' did not resolve framework metadata. Ensure the repository is restored with a compatible SDK.",
                affectsCompleteness: true);
        }
    }

    private void CreateProjectDependencies()
    {
        var contextsByRoslynId = projectContexts.ToDictionary(static context => context.Project.Id);
        foreach (var context in projectContexts)
        {
            foreach (var reference in context.Project.ProjectReferences)
            {
                if (!contextsByRoslynId.TryGetValue(reference.ProjectId, out var target) ||
                    context.ProjectId == target.ProjectId)
                {
                    continue;
                }

                AddSemanticRelationship(
                    KnownRelationshipKinds.ProjectDependsOn,
                    context.ProjectId,
                    target.ProjectId,
                    KnownEvidenceKinds.ProjectMetadata,
                    $"MSBuild project '{context.RelativePath}' references '{target.RelativePath}'.",
                    []);
            }
        }
    }

    private void ExtractDeclarations(CancellationToken cancellationToken)
    {
        foreach (var context in projectContexts)
        {
            foreach (var document in context.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var declarations = document.Root.DescendantNodes()
                    .Where(static node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                    .OrderBy(static node => node.SpanStart);

                foreach (var declaration in declarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    INamedTypeSymbol? type = declaration switch
                    {
                        BaseTypeDeclarationSyntax baseType =>
                            document.SemanticModel.GetDeclaredSymbol(baseType, cancellationToken) as INamedTypeSymbol,
                        DelegateDeclarationSyntax delegateType =>
                            document.SemanticModel.GetDeclaredSymbol(delegateType, cancellationToken) as INamedTypeSymbol,
                        _ => null
                    };
                    if (type is null || type.IsImplicitlyDeclared)
                    {
                        continue;
                    }

                    AddDeclaredSymbol(context, type, declaration.GetLocation(), document.File);
                    foreach (var member in type.GetMembers()
                                 .Where(static member => !member.IsImplicitlyDeclared && member is not INamedTypeSymbol)
                                 .OrderBy(RoslynSymbolIdentity.GetSignature, StringComparer.Ordinal))
                    {
                        foreach (var memberLocation in member.Locations.Where(static location => location.IsInSource))
                        {
                            if (fileIndex.TryCreateLocation(memberLocation) is not { } sourceLocation ||
                                sourceLocation.SourceFileId is not { } memberFileId)
                            {
                                continue;
                            }

                            AddDeclaredSymbol(context, member, memberLocation, new IndexedSourceFile(
                                memberFileId,
                                sourceLocation.RelativePath,
                                GetSourceFileHash(memberFileId)));
                        }
                    }
                }
            }
        }

    }

    private void AddDeclaredSymbol(
        ProjectContext context,
        ISymbol symbol,
        Location declarationLocation,
        IndexedSourceFile indexedFile)
    {
        var location = fileIndex.TryCreateLocation(declarationLocation);
        if (location is null)
        {
            return;
        }

        var id = RoslynSymbolIdentity.CreateId(context.RelativePath, symbol);
        if (!symbols.TryGetValue(id, out var fact))
        {
            fact = new MutableSymbolFact(
                id,
                symbol.Name,
                symbol is IMethodSymbol ? KnownSymbolKinds.Callable : symbol is INamedTypeSymbol ? KnownSymbolKinds.Type : KnownSymbolKinds.Value,
                RoslynSymbolIdentity.GetVisibility(symbol),
                RoslynSymbolIdentity.GetNativeKind(symbol),
                RoslynSymbolIdentity.GetQualifiedName(symbol),
                RoslynSymbolIdentity.GetSignature(symbol),
                context.AssemblyName);
            symbols.Add(id, fact);
        }

        fact.AddDeclaration(location, indexedFile.Id);
        if (symbol.ContainingType is not null)
        {
            fact.ContainingTypeIds.Add(RoslynSymbolIdentity.CreateId(context.RelativePath, symbol.ContainingType));
        }

        declaredSymbols.Add(new DeclaredSymbolObservation(symbol, id));
    }

    private void ExtractStructuralRelationships()
    {
        foreach (var symbol in symbols.Values.OrderBy(static symbol => symbol.Id))
        {
            evidence.TryAdd(symbol.EvidenceId, symbol.ToEvidence());
            if (symbol.ContainingTypeIds.Count > 0)
            {
                foreach (var containingTypeId in symbol.ContainingTypeIds)
                {
                    if (symbols.ContainsKey(containingTypeId))
                    {
                        AddStructuralRelationship(
                            KnownRelationshipKinds.Defines,
                            containingTypeId,
                            symbol.Id,
                            [symbol.EvidenceId]);
                    }
                }

                continue;
            }

            foreach (var fileId in symbol.DeclaringFileIds)
            {
                AddStructuralRelationship(
                    KnownRelationshipKinds.Defines,
                    fileId,
                    symbol.Id,
                    [symbol.EvidenceId]);
            }
        }
    }

    private void ExtractSemanticRelationships(CancellationToken cancellationToken)
    {
        var lookup = new SymbolLookup();
        foreach (var symbol in symbols.Values)
        {
            lookup.Add(symbol);
        }

        foreach (var observation in declaredSymbols
                     .OrderBy(static observation => observation.Id)
                     .ThenBy(static observation => observation.Symbol.Kind))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!symbols.TryGetValue(observation.Id, out var sourceFact))
            {
                continue;
            }

            if (observation.Symbol is INamedTypeSymbol type)
            {
                AddTypeRelationships(type, sourceFact, lookup);
            }
            else
            {
                AddSignatureReferences(observation.Symbol, sourceFact, lookup);
            }
        }

        foreach (var context in projectContexts)
        {
            foreach (var document in context.Documents)
            {
                ExtractCalls(document, lookup, cancellationToken);
            }
        }
    }

    private void AddTypeRelationships(
        INamedTypeSymbol type,
        MutableSymbolFact sourceFact,
        SymbolLookup lookup)
    {
        if (lookup.TryResolve(type.BaseType, out var baseTypeId))
        {
            AddSemanticRelationship(
                KnownRelationshipKinds.Inherits,
                sourceFact.Id,
                baseTypeId,
                KnownEvidenceKinds.SemanticBinding,
                $"Roslyn bound '{sourceFact.QualifiedName}' to its direct base type.",
                sourceFact.Locations);
        }

        foreach (var @interface in type.Interfaces)
        {
            if (!lookup.TryResolve(@interface, out var interfaceId))
            {
                continue;
            }

            var relationshipKind = type.TypeKind == TypeKind.Interface
                ? KnownRelationshipKinds.Inherits
                : KnownRelationshipKinds.Implements;
            AddSemanticRelationship(
                relationshipKind,
                sourceFact.Id,
                interfaceId,
                KnownEvidenceKinds.SemanticBinding,
                $"Roslyn bound '{sourceFact.QualifiedName}' to direct interface '{@interface.ToDisplayString()}'.",
                sourceFact.Locations);
        }
    }

    private void AddSignatureReferences(
        ISymbol symbol,
        MutableSymbolFact sourceFact,
        SymbolLookup lookup)
    {
        foreach (var referencedType in EnumerateReferencedTypes(symbol))
        {
            if (!lookup.TryResolve(referencedType, out var targetId))
            {
                continue;
            }

            AddSemanticRelationship(
                KnownRelationshipKinds.References,
                sourceFact.Id,
                targetId,
                KnownEvidenceKinds.SemanticBinding,
                $"The declared signature of '{sourceFact.QualifiedName}' references an internal type.",
                sourceFact.Locations);
        }
    }

    private void ExtractCalls(
        DocumentContext document,
        SymbolLookup lookup,
        CancellationToken cancellationToken)
    {
        var callSites = document.Root.DescendantNodes()
            .Where(static node => node is
                InvocationExpressionSyntax or
                ObjectCreationExpressionSyntax or
                ImplicitObjectCreationExpressionSyntax or
                ConstructorInitializerSyntax)
            .OrderBy(static node => node.SpanStart);

        foreach (var callSite in callSites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = document.SemanticModel.GetSymbolInfo(callSite, cancellationToken).Symbol;
            if (!lookup.TryResolve(target, out var targetId) ||
                !symbols.TryGetValue(targetId, out var targetFact) ||
                targetFact.SymbolKind != KnownSymbolKinds.Callable)
            {
                continue;
            }

            var containingSymbol = document.SemanticModel.GetEnclosingSymbol(callSite.SpanStart, cancellationToken);
            if (!TryResolveContainingSymbol(containingSymbol, lookup, out var sourceId) ||
                !symbols.TryGetValue(sourceId, out var sourceFact) ||
                sourceFact.SymbolKind != KnownSymbolKinds.Callable ||
                fileIndex.TryCreateLocation(callSite.GetLocation()) is not { } location)
            {
                continue;
            }

            AddSemanticRelationship(
                KnownRelationshipKinds.Calls,
                sourceId,
                targetId,
                KnownEvidenceKinds.SemanticBinding,
                $"Roslyn bound a call from '{sourceFact.QualifiedName}' to '{targetFact.QualifiedName}'.",
                [location]);
        }
    }

    private static bool TryResolveContainingSymbol(
        ISymbol? symbol,
        SymbolLookup lookup,
        out StructuralId id)
    {
        while (symbol is not null)
        {
            if (lookup.TryResolve(symbol, out id))
            {
                return true;
            }

            symbol = symbol.ContainingSymbol;
        }

        id = default;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateReferencedTypes(ISymbol symbol)
    {
        var roots = symbol switch
        {
            IMethodSymbol method => method.Parameters.Select(static parameter => parameter.Type)
                .Prepend(method.ReturnType),
            IPropertySymbol property => property.Parameters.Select(static parameter => parameter.Type)
                .Prepend(property.Type),
            IFieldSymbol field => [field.Type],
            IEventSymbol @event => [@event.Type],
            _ => []
        };
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var root in roots)
        {
            foreach (var referencedType in ExpandType(root, visited))
            {
                yield return referencedType;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> ExpandType(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type))
        {
            yield break;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (var nested in ExpandType(array.ElementType, visited))
                {
                    yield return nested;
                }

                break;
            case IPointerTypeSymbol pointer:
                foreach (var nested in ExpandType(pointer.PointedAtType, visited))
                {
                    yield return nested;
                }

                break;
            case INamedTypeSymbol namedType:
                yield return namedType.OriginalDefinition;
                foreach (var typeArgument in namedType.TypeArguments)
                {
                    foreach (var nested in ExpandType(typeArgument, visited))
                    {
                        yield return nested;
                    }
                }

                break;
            case ITypeParameterSymbol typeParameter:
                foreach (var constraintType in typeParameter.ConstraintTypes)
                {
                    foreach (var nested in ExpandType(constraintType, visited))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private void EnsureProjectFact(StructuralId projectId, string relativePath)
    {
        if (projects.ContainsKey(projectId))
        {
            return;
        }

        var evidenceId = GetProjectEvidenceId(projectId);
        projects.Add(
            projectId,
            new ProjectFact(
                projectId,
                Path.GetFileNameWithoutExtension(relativePath),
                "csharp",
                relativePath,
                [evidenceId]));
        evidence.TryAdd(
            evidenceId,
            new EvidenceFact(
                evidenceId,
                KnownEvidenceKinds.ProjectMetadata,
                CSharpLanguageAnalyzerId.Value,
                $"MSBuild {registration.Version} loaded C# project '{relativePath}'.",
                ConfidenceScore.Certain));
    }

    private void EnsureSourceFileFact(IndexedSourceFile indexedFile)
    {
        if (sourceFiles.ContainsKey(indexedFile.Id))
        {
            return;
        }

        var evidenceId = GetSourceFileEvidenceId(indexedFile.Id);
        sourceFiles.Add(
            indexedFile.Id,
            new SourceFileFact(
                indexedFile.Id,
                indexedFile.RelativePath,
                "csharp",
                indexedFile.ContentHash,
                "sha256",
                [evidenceId]));
        evidence.TryAdd(
            evidenceId,
            new EvidenceFact(
                evidenceId,
                KnownEvidenceKinds.RepositoryMetadata,
                CSharpLanguageAnalyzerId.Value,
                $"Repository discovery observed C# source file '{indexedFile.RelativePath}'.",
                ConfidenceScore.Certain));
    }

    private void AddStructuralRelationship(
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        IEnumerable<StructuralId> supportingEvidenceIds)
    {
        var relationship = GetOrCreateRelationship(kind, sourceId, targetId);
        relationship.AddSupportingEvidence(supportingEvidenceIds);
    }

    private void AddSemanticRelationship(
        string kind,
        StructuralId sourceId,
        StructuralId targetId,
        string evidenceKind,
        string description,
        IEnumerable<SourceLocation> locations)
    {
        var relationship = GetOrCreateRelationship(kind, sourceId, targetId);
        relationship.AddSemanticEvidence(evidenceKind, description, locations);
    }

    private MutableRelationshipFact GetOrCreateRelationship(
        string kind,
        StructuralId sourceId,
        StructuralId targetId)
    {
        var relationshipId = StructuralId.Create("relationship", kind, sourceId.Value, targetId.Value);
        if (!relationships.TryGetValue(relationshipId, out var relationship))
        {
            relationship = new MutableRelationshipFact(kind, sourceId, targetId);
            relationships.Add(relationshipId, relationship);
        }

        return relationship;
    }

    private LanguageAnalysisResult BuildResult(string status)
    {
        var relationshipFacts = new List<RelationshipFact>(relationships.Count);
        var relationshipEvidence = new List<EvidenceFact>();
        foreach (var relationship in relationships.Values.OrderBy(static relationship => relationship.Id))
        {
            relationshipFacts.Add(relationship.ToFact(relationshipEvidence));
        }

        foreach (var item in relationshipEvidence)
        {
            evidence.TryAdd(item.Id, item);
        }

        var metadata = new AnalyzerRunMetadata(
            CSharpLanguageAnalyzerId.Value,
            "Scout C# Analyzer",
            analyzerVersion,
            "csharp",
            status,
            DateTimeOffset.UtcNow,
            positionEncoding);
        return new LanguageAnalysisResult(
            metadata,
            projects.Values,
            sourceFiles.Values,
            symbols.Values.Select(static symbol => symbol.ToFact()),
            relationshipFacts,
            evidence.Values,
            diagnostics.ToFacts());
    }

    private void DrainWorkspaceDiagnostics(ConcurrentQueue<WorkspaceDiagnostic> workspaceDiagnostics)
    {
        while (workspaceDiagnostics.TryDequeue(out var diagnostic))
        {
            diagnostics.AddWorkspaceDiagnostic(diagnostic);
        }
    }

    private bool TryGetProjectRelativePath(string? projectFilePath, out string relativePath)
    {
        if (string.IsNullOrWhiteSpace(projectFilePath))
        {
            relativePath = string.Empty;
            return false;
        }

        try
        {
            var candidate = RepositoryPaths.GetRelativePath(repository.RootPath, projectFilePath);
            return projectPaths.TryGetValue(candidate, out relativePath!);
        }
        catch (ArgumentException)
        {
            relativePath = string.Empty;
            return false;
        }
    }

    private string GetSourceFileHash(StructuralId sourceFileId) =>
        sourceFiles.TryGetValue(sourceFileId, out var sourceFile) && sourceFile.ContentHash is not null
            ? sourceFile.ContentHash
            : throw new InvalidOperationException($"Source file '{sourceFileId}' was not indexed.");

    private static StructuralId GetProjectEvidenceId(StructuralId projectId) =>
        StructuralId.Create("evidence", CSharpLanguageAnalyzerId.Value, "project", projectId.Value);

    private static StructuralId GetSourceFileEvidenceId(StructuralId sourceFileId) =>
        StructuralId.Create("evidence", CSharpLanguageAnalyzerId.Value, "source-file", sourceFileId.Value);

    private sealed class ProjectContext
    {
        public ProjectContext(
            Project project,
            string relativePath,
            StructuralId projectId,
            string assemblyName)
        {
            Project = project;
            RelativePath = relativePath;
            ProjectId = projectId;
            AssemblyName = assemblyName;
        }

        public Project Project { get; }

        public string RelativePath { get; }

        public StructuralId ProjectId { get; }

        public string AssemblyName { get; }

        public List<DocumentContext> Documents { get; } = [];
    }

    private sealed record DocumentContext(
        SyntaxNode Root,
        SemanticModel SemanticModel,
        IndexedSourceFile File);

    private sealed record DeclaredSymbolObservation(
        ISymbol Symbol,
        StructuralId Id);
}

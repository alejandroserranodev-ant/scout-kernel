using System.Collections.ObjectModel;
using Scout.Core.Analysis;
using Scout.Core.Graph;
using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Kernel;
using Scout.Core.Repository;
using Scout.Core.Retrieval;

namespace Scout.Core.Compilation;

/// <summary>
/// Orchestrates repository discovery, language analysis, graph validation, intelligence, and kernel generation.
/// </summary>
public sealed class ScoutCompiler
{
    private readonly ReadOnlyCollection<ILanguageAnalyzer> analyzers;
    private readonly TimeProvider timeProvider;
    private readonly string scoutVersion;
    private readonly IRetrievalIndexWriter? indexWriter;
    private readonly IDenseEmbeddingModel? denseModel;

    /// <summary>
    /// Initializes the compilation orchestrator with active language analyzers.
    /// </summary>
    /// <param name="analyzers">The language analyzers available for this process.</param>
    /// <param name="timeProvider">The optional clock used for reproducible snapshots.</param>
    /// <param name="scoutVersion">The optional compiler version recorded in the manifest.</param>
    /// <param name="indexWriter">
    /// The optional retrieval index writer. Supplying one publishes a queryable index inside the kernel, which
    /// is what lets an agent ask a question instead of reading the kernel's index files.
    /// </param>
    /// <param name="denseModel">
    /// An optional dense embedding model that adds semantic vectors to the retrieval index. Omitted by default,
    /// so a compile needs no model, no network and no service.
    /// </param>
    public ScoutCompiler(
        IEnumerable<ILanguageAnalyzer> analyzers,
        TimeProvider? timeProvider = null,
        string? scoutVersion = null,
        IRetrievalIndexWriter? indexWriter = null,
        IDenseEmbeddingModel? denseModel = null)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        this.analyzers = new ReadOnlyCollection<ILanguageAnalyzer>(
            analyzers.OrderBy(analyzer => analyzer.AnalyzerId, StringComparer.Ordinal).ToArray());
        if (this.analyzers.Count == 0)
        {
            throw new ArgumentException("At least one language analyzer is required.", nameof(analyzers));
        }

        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.indexWriter = indexWriter;
        this.denseModel = denseModel;
        this.scoutVersion = string.IsNullOrWhiteSpace(scoutVersion)
            ? typeof(ScoutCompiler).Assembly.GetName().Version?.ToString(3) ?? "unknown"
            : scoutVersion.Trim();
    }

    /// <summary>
    /// Compiles one source repository into a compact Scout Kernel.
    /// </summary>
    /// <param name="request">The repository and optional output path.</param>
    /// <param name="cancellationToken">A token that cancels compilation before publication.</param>
    /// <returns>All compilation stages and the generated kernel.</returns>
    public async Task<ScoutCompilationResult> CompileAsync(
        ScoutCompileRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RepositoryPath);

        var extensions = analyzers
            .SelectMany(analyzer => analyzer.SupportedFileExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var repository = await new RepositoryDiscovery(extensions)
            .DiscoverAsync(request.RepositoryPath, cancellationToken)
            .ConfigureAwait(false);
        var applicableAnalyzers = analyzers
            .Where(analyzer => analyzer.SupportedFileExtensions.Any(extension =>
                repository.Files.Any(file => string.Equals(
                    Path.GetExtension(file.RelativePath),
                    NormalizeExtension(extension),
                    StringComparison.OrdinalIgnoreCase))))
            .ToArray();

        if (applicableAnalyzers.Length == 0)
        {
            throw new InvalidOperationException("No configured language analyzer applies to the discovered repository inputs.");
        }

        var contributions = new List<LanguageAnalysisResult>(applicableAnalyzers.Length);
        foreach (var analyzer in applicableAnalyzers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            contributions.Add(await analyzer.AnalyzeAsync(repository, cancellationToken).ConfigureAwait(false));
        }

        if (contributions.All(contribution => contribution.Analyzer.Status == KnownAnalyzerStatuses.Failed))
        {
            var reason = contributions
                .SelectMany(contribution => contribution.Diagnostics)
                .FirstOrDefault(diagnostic => diagnostic.Severity == KnownDiagnosticSeverities.Error)
                ?.Message ?? "Every applicable language analyzer failed.";
            throw new InvalidOperationException($"Repository analysis failed: {reason}");
        }

        if (!contributions.SelectMany(contribution => contribution.Projects).Any())
        {
            throw new InvalidOperationException("Repository analysis did not discover a loadable source project.");
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var revisionKind = repository.Revision.Kind == "git-commit"
            ? KnownRevisionKinds.GitCommit
            : KnownRevisionKinds.Unknown;
        var source = new SourceIdentity(
            StructuralId.Create("repository", repository.Name),
            repository.Name,
            revisionKind,
            repository.Revision.Value,
            repository.ContentFingerprint);
        var declaredIds = contributions
            .SelectMany(contribution => contribution.Projects.Select(fact => fact.Id)
                .Concat(contribution.SourceFiles.Select(fact => fact.Id))
                .Concat(contribution.Symbols.Select(fact => fact.Id)))
            .ToHashSet();
        var unresolvedDiagnostics = new List<AnalysisDiagnostic>();
        var relationships = ResolveRelationships(contributions, declaredIds, unresolvedDiagnostics);
        var structural = new StructuralSnapshot(
            source,
            createdAtUtc,
            contributions.Select(contribution => contribution.Analyzer),
            contributions.SelectMany(contribution => contribution.Projects),
            contributions.SelectMany(contribution => contribution.SourceFiles),
            contributions.SelectMany(contribution => contribution.Symbols),
            relationships,
            contributions.SelectMany(contribution => contribution.Evidence),
            contributions.SelectMany(contribution => contribution.Diagnostics).Concat(unresolvedDiagnostics));
        var graph = new ProjectGraph(structural);
        var intelligence = BasicIntelligenceCompiler.Compile(graph, createdAtUtc);
        var outputPath = string.IsNullOrWhiteSpace(request.OutputPath)
            ? GetDefaultOutputPath(repository.RootPath)
            : Path.GetFullPath(request.OutputPath);
        var kernel = await ScoutKernelGenerator.GenerateAsync(
            repository,
            structural,
            intelligence,
            outputPath,
            scoutVersion,
            indexWriter,
            denseModel,
            cancellationToken).ConfigureAwait(false);

        return new ScoutCompilationResult(repository, structural, graph, intelligence, kernel);
    }

    private static string NormalizeExtension(string extension)
    {
        return extension.StartsWith('.') ? extension : $".{extension}";
    }

    /// <summary>
    /// Keeps only the relationships whose endpoints were actually declared by some analyzer.
    /// </summary>
    /// <remarks>
    /// An analyzer may observe a reference whose declaration belongs to another language, and that other
    /// analyzer does not always run. Dropping the dangling relationship and recording why keeps the unresolved
    /// reference explicit, which is Scout's contract, instead of failing the whole compilation or fabricating
    /// the missing declaration.
    /// </remarks>
    private static List<RelationshipFact> ResolveRelationships(
        IReadOnlyList<LanguageAnalysisResult> contributions,
        HashSet<StructuralId> declaredIds,
        List<AnalysisDiagnostic> diagnostics)
    {
        var resolved = new List<RelationshipFact>();
        var reportedKinds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contribution in contributions)
        {
            var unresolvedCount = 0;
            foreach (var relationship in contribution.Relationships)
            {
                if (declaredIds.Contains(relationship.SourceId) && declaredIds.Contains(relationship.TargetId))
                {
                    resolved.Add(relationship);
                    continue;
                }

                unresolvedCount++;
                reportedKinds.Add(relationship.Kind);
            }

            if (unresolvedCount == 0)
            {
                continue;
            }

            var analyzerId = contribution.Analyzer.AnalyzerId;
            var kinds = string.Join(", ", reportedKinds.Order(StringComparer.Ordinal));
            var message =
                $"{unresolvedCount} observed relationship(s) of kind(s) {kinds} were dropped because their " +
                "declaration was not analyzed. Enable the analyzer that owns the referenced language to resolve them.";
            diagnostics.Add(new AnalysisDiagnostic(
                StructuralId.Create("diagnostic", analyzerId, "UNRESOLVED_RELATIONSHIP_ENDPOINT", message),
                analyzerId,
                KnownDiagnosticSeverities.Warning,
                "UNRESOLVED_RELATIONSHIP_ENDPOINT",
                message));
            reportedKinds.Clear();
        }

        return resolved;
    }

    private static string GetDefaultOutputPath(string repositoryRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var parent = Path.GetDirectoryName(root)
            ?? throw new InvalidOperationException("A repository root must have a parent directory.");
        var name = Path.GetFileName(root);
        return Path.Combine(parent, $"{name}.scout");
    }
}

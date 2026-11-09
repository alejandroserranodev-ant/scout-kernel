using System.Globalization;
using System.Text.RegularExpressions;
using Scout.Core.Graph;
using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Derives deliberately conservative first-slice intelligence from a validated project graph.
/// </summary>
public static partial class BasicIntelligenceCompiler
{
    private const int MinimumConventionPopulation = 4;
    private const int MinimumConventionSupport = 3;
    private const double MinimumConventionAdherence = 0.75;

    private static readonly string[] BaseUnknownAreas =
    [
        "canonical-examples",
        "concerns",
        "flows",
        "git-history-freshness",
        "routing"
    ];

    /// <summary>
    /// Compiles evidence-backed intelligence without changing the structural source of truth.
    /// </summary>
    /// <param name="graph">The validated graph derived from the structural snapshot.</param>
    /// <param name="createdAtUtc">The intelligence creation timestamp.</param>
    /// <returns>The deterministic intelligence snapshot.</returns>
    public static IntelligenceSnapshot Compile(ProjectGraph graph, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var modules = CompileModules(graph);
        var conventions = CompileNamingConventions(graph.Snapshot);
        conventions.AddRange(CompileFolderStructureConventions(graph.Snapshot));
        var contracts = ContractCompiler.Compile(graph, modules);
        string[] unknownAreas = contracts.Length == 0
            ? [.. BaseUnknownAreas, "contracts"]
            : BaseUnknownAreas;
        return new IntelligenceSnapshot(
            createdAtUtc,
            modules,
            conventions,
            contracts,
            unknownAreas,
            documentedIntents: DocumentationCompiler.Compile(graph.Snapshot));
    }

    private static ModuleIntelligence[] CompileModules(ProjectGraph graph)
    {
        var moduleIdsByProject = graph.Snapshot.Projects.ToDictionary(
            project => project.Id,
            project => StructuralId.Create("module", project.Id.Value));

        return graph.Snapshot.Projects.Select(project =>
        {
            var projectEdges = graph.GetOutgoingEdges(project.Id);
            var sourceFileIds = projectEdges
                .Where(edge => edge.Kind == KnownRelationshipKinds.Contains &&
                               graph.GetNode(edge.TargetId) is SourceFileFact)
                .Select(edge => edge.TargetId)
                .ToHashSet();
            var symbolCount = sourceFileIds.Sum(sourceFileId =>
                graph.GetOutgoingEdges(sourceFileId, KnownRelationshipKinds.Defines).Count);
            var dependencyModuleIds = projectEdges
                .Where(edge => edge.Kind == KnownRelationshipKinds.ProjectDependsOn)
                .Select(edge => moduleIdsByProject.GetValueOrDefault(edge.TargetId))
                .Where(moduleId => !moduleId.IsEmpty)
                .ToArray();
            var scope = RepositoryScope.FromRelativePath(project.RelativePath);
            var summary = string.Create(
                CultureInfo.InvariantCulture,
                $"{project.LanguageId} project with {sourceFileIds.Count} source files and {symbolCount} declared symbols.");

            return new ModuleIntelligence(
                moduleIdsByProject[project.Id],
                project.Name,
                summary,
                project.Id,
                project.RelativePath,
                sourceFileIds.Count,
                symbolCount,
                dependencyModuleIds,
                new KnowledgeMetadata(
                    KnowledgeClassification.Inference,
                    new ConfidenceScore(0.7),
                    scope,
                    KnowledgeFreshness.Unknown,
                    project.EvidenceIds));
        }).ToArray();
    }

    private static List<ConventionIntelligence> CompileNamingConventions(StructuralSnapshot snapshot)
    {
        var typesByDirectory = snapshot.Symbols
            .Where(symbol => symbol.SymbolKind == KnownSymbolKinds.Type && symbol.DeclarationLocations.Count > 0)
            .GroupBy(
                symbol => GetDirectory(symbol.DeclarationLocations[0].RelativePath),
                StringComparer.Ordinal);
        var conventions = new List<ConventionIntelligence>();

        foreach (var scopeGroup in typesByDirectory.OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var candidates = scopeGroup.OrderBy(symbol => symbol.Id).ToArray();
            if (candidates.Length < MinimumConventionPopulation)
            {
                continue;
            }

            var suffixGroups = candidates
                .Select(symbol => (Symbol: symbol, Suffix: GetTerminalNameToken(symbol.Name)))
                .Where(item => item.Suffix.Length >= 3 && item.Suffix.Length < item.Symbol.Name.Length)
                .GroupBy(item => item.Suffix, StringComparer.Ordinal)
                .Select(group => new
                {
                    Suffix = group.Key,
                    Symbols = group.Select(item => item.Symbol).OrderBy(symbol => symbol.Id).ToArray()
                })
                .Where(group => group.Symbols.Length >= MinimumConventionSupport)
                .OrderByDescending(group => group.Symbols.Length)
                .ThenBy(group => group.Suffix, StringComparer.Ordinal)
                .ToArray();

            foreach (var suffixGroup in suffixGroups)
            {
                var adherence = (double)suffixGroup.Symbols.Length / candidates.Length;
                if (adherence < MinimumConventionAdherence)
                {
                    continue;
                }

                var supportingIds = suffixGroup.Symbols.SelectMany(symbol => symbol.EvidenceIds);
                var exceptionIds = candidates
                    .Except(suffixGroup.Symbols)
                    .Select(symbol => symbol.Id);
                var scope = scopeGroup.Key == "." ? "**" : $"{scopeGroup.Key}/**";
                var conventionId = StructuralId.Create(
                    "convention",
                    scope,
                    KnownSymbolKinds.Type,
                    suffixGroup.Suffix);

                conventions.Add(new ConventionIntelligence(
                    conventionId,
                    $"Type names in this scope end with '{suffixGroup.Suffix}'.",
                    $"*{suffixGroup.Suffix}",
                    suffixGroup.Symbols.Length,
                    candidates.Length,
                    new KnowledgeMetadata(
                        KnowledgeClassification.Inference,
                        new ConfidenceScore(adherence),
                        scope,
                        KnowledgeFreshness.Unknown,
                        supportingIds,
                        exceptionIds)));
            }
        }

        return conventions;
    }

    /// <summary>
    /// Derives conventions from repeated directory shapes: sibling folders under the same parent
    /// that consistently contain the same set of immediate files and subfolders (e.g. every
    /// feature folder pairing an `actions.ts` with a `components/` subfolder). This is what makes
    /// a folder like `features/` legible as a role rather than an arbitrary grouping.
    /// </summary>
    private static List<ConventionIntelligence> CompileFolderStructureConventions(StructuralSnapshot snapshot)
    {
        var conventions = new List<ConventionIntelligence>();
        var (allDirectories, filesByDirectory) = CollectDirectories(snapshot);
        var childDirectoriesByParent = allDirectories
            .Where(directory => directory != ".")
            .GroupBy(GetParentDirectory, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(directory => directory, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);

        foreach (var parent in childDirectoriesByParent.Keys.OrderBy(key => key, StringComparer.Ordinal))
        {
            var children = childDirectoriesByParent[parent];
            if (children.Length < MinimumConventionPopulation)
            {
                continue;
            }

            var entriesByChild = children.ToDictionary(
                directory => directory,
                directory => BuildDirectoryEntrySet(directory, filesByDirectory, childDirectoriesByParent),
                StringComparer.Ordinal);

            // A shared "core shape" is built from entries that, on their own, already clear the
            // adherence bar (present in most siblings) — not an exact full-signature match, since
            // one extra local file (e.g. a feature's own `status.ts`) shouldn't disqualify the
            // pattern shared by everything else in that folder.
            var candidateEntries = entriesByChild.Values
                .SelectMany(entries => entries)
                .Distinct(StringComparer.Ordinal)
                .Select(entry => (
                    Entry: entry,
                    Support: entriesByChild.Values.Count(entries => entries.Contains(entry))))
                .Where(item => item.Support >= MinimumConventionSupport &&
                               (double)item.Support / children.Length >= MinimumConventionAdherence)
                .OrderByDescending(item => item.Support)
                .ThenBy(item => item.Entry, StringComparer.Ordinal)
                .Select(item => item.Entry)
                .ToArray();

            if (candidateEntries.Length == 0)
            {
                continue;
            }

            var matchingDirectories = children
                .Where(directory => candidateEntries.All(entry => entriesByChild[directory].Contains(entry)))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .ToArray();
            var adherence = (double)matchingDirectories.Length / children.Length;
            if (matchingDirectories.Length < MinimumConventionSupport || adherence < MinimumConventionAdherence)
            {
                continue;
            }

            var pattern = string.Join(", ", candidateEntries);
            var evidenceIds = matchingDirectories
                .SelectMany(directory => filesByDirectory.GetValueOrDefault(directory, []))
                .SelectMany(file => file.EvidenceIds);
            var scope = parent == "." ? "**" : $"{parent}/**";
            var parentLabel = parent == "." ? "the repository root" : $"'{parent}'";
            var conventionId = StructuralId.Create("convention", scope, "directory-shape", pattern);

            conventions.Add(new ConventionIntelligence(
                conventionId,
                $"Sibling folders directly under {parentLabel} typically each contain: {pattern}.",
                pattern,
                matchingDirectories.Length,
                children.Length,
                new KnowledgeMetadata(
                    KnowledgeClassification.Inference,
                    new ConfidenceScore(adherence),
                    scope,
                    KnowledgeFreshness.Unknown,
                    evidenceIds)));
        }

        return conventions;
    }

    private static (HashSet<string> Directories, Dictionary<string, SourceFileFact[]> FilesByDirectory) CollectDirectories(
        StructuralSnapshot snapshot)
    {
        var directories = new HashSet<string>(StringComparer.Ordinal) { "." };
        foreach (var file in snapshot.SourceFiles)
        {
            var cursor = RepositoryScope.DirectoryOf(file.RelativePath);
            while (directories.Add(cursor) && cursor != ".")
            {
                cursor = GetParentDirectory(cursor);
            }
        }

        var filesByDirectory = snapshot.SourceFiles
            .GroupBy(file => RepositoryScope.DirectoryOf(file.RelativePath), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return (directories, filesByDirectory);
    }

    private static HashSet<string> BuildDirectoryEntrySet(
        string directory,
        Dictionary<string, SourceFileFact[]> filesByDirectory,
        Dictionary<string, string[]> childDirectoriesByParent)
    {
        var entries = new HashSet<string>(StringComparer.Ordinal);
        if (filesByDirectory.TryGetValue(directory, out var files))
        {
            foreach (var file in files)
            {
                entries.Add(RepositoryPath.GetName(file.RelativePath));
            }
        }

        if (childDirectoriesByParent.TryGetValue(directory, out var subdirectories))
        {
            foreach (var subdirectory in subdirectories)
            {
                entries.Add(GetImmediateName(subdirectory) + "/");
            }
        }

        return entries;
    }

    private static string GetParentDirectory(string directory)
    {
        if (directory == ".")
        {
            return ".";
        }

        var separator = directory.LastIndexOf('/');
        return separator < 0 ? "." : directory[..separator];
    }

    private static string GetImmediateName(string directory)
    {
        var separator = directory.LastIndexOf('/');
        return separator < 0 ? directory : directory[(separator + 1)..];
    }

    private static string GetDirectory(string relativePath) => RepositoryScope.DirectoryOf(relativePath);

    private static string GetTerminalNameToken(string name)
    {
        var matches = IdentifierTokenRegex().Matches(name);
        return matches.Count == 0 ? name : matches[^1].Value;
    }

    [GeneratedRegex("[A-Z]+(?=[A-Z][a-z]|[0-9]|$)|[A-Z]?[a-z]+|[0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierTokenRegex();
}

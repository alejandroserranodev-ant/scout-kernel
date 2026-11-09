using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scout.Core.Graph;
using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Kernel;
using Scout.Core.Repository;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Intelligence;
using Scout.Tests.Support;

namespace Scout.Tests.Kernel;

public sealed partial class ScoutKernelGeneratorTests
{
    private const string FixtureName = "MinimalCSharpRepository";
    private const string ScoutVersion = "0.1.0-test";

    [Fact]
    public async Task GenerateAsyncProducesSmallReferenceDrivenKernelWithoutSourceBodies()
    {
        const string sourceBodyMarker = "SOURCE_BODY_MUST_NOT_BE_COPIED_73A90F";
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "kernel source");
        repositoryFiles.WriteFile(
            "src/Billing/SourceBodyMarker.cs",
            $"namespace Billing; internal sealed class SourceBodyMarker {{ private const string Value = \"{sourceBodyMarker}\"; }}");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var outputPath = Path.Combine(repositoryFiles.WorkspacePath, "generated kernel.scout");

        var result = await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion);

        Assert.InRange(new FileInfo(Path.Combine(outputPath, "KERNEL.md")).Length, 1L, 2048L);
        Assert.InRange(new FileInfo(Path.Combine(outputPath, "AGENTS.md")).Length, 1L, 1536L);
        Assert.All(result.ArtifactPaths, path =>
        {
            Assert.False(Path.IsPathRooted(path));
            Assert.DoesNotContain('\\', path);
        });

        foreach (var artifactPath in result.ArtifactPaths.Where(IsPublicArtifact))
        {
            var content = await File.ReadAllTextAsync(ResolveArtifact(outputPath, artifactPath));
            Assert.False(content.Contains(sourceBodyMarker, StringComparison.Ordinal));
        }

        var references = ReadPublicReferences(outputPath, result.ArtifactPaths);
        Assert.NotEmpty(references);
        Assert.All(references, reference => AssertReferenceResolves(outputPath, reference));
    }

    [Fact]
    public async Task GenerateAsyncProducesDeterministicBytesForFixedSnapshots()
    {
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "deterministic kernel source");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var firstOutputPath = Path.Combine(repositoryFiles.WorkspacePath, "first.scout");
        var secondOutputPath = Path.Combine(repositoryFiles.WorkspacePath, "second.scout");

        var first = await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            firstOutputPath,
            ScoutVersion);
        var second = await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            secondOutputPath,
            ScoutVersion);

        Assert.Equal(first.ArtifactPaths.ToArray(), second.ArtifactPaths.ToArray());
        foreach (var artifactPath in first.ArtifactPaths)
        {
            var firstBytes = await File.ReadAllBytesAsync(ResolveArtifact(firstOutputPath, artifactPath));
            var secondBytes = await File.ReadAllBytesAsync(ResolveArtifact(secondOutputPath, artifactPath));
            Assert.Equal(firstBytes, secondBytes);
        }
    }

    [Fact]
    public async Task GenerateAsyncRefusesToReplaceNonKernelDirectory()
    {
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "protected output source");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var outputPath = Path.Combine(repositoryFiles.WorkspacePath, "existing output");
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "keep.txt");
        await File.WriteAllTextAsync(sentinelPath, "user-owned-content", new UTF8Encoding(false));

        var exception = await Assert.ThrowsAsync<IOException>(() => ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion));

        Assert.Contains("not a Scout Kernel", exception.Message, StringComparison.Ordinal);
        Assert.Equal("user-owned-content", await File.ReadAllTextAsync(sentinelPath));
        Assert.False(File.Exists(Path.Combine(outputPath, "manifest.json")));
        Assert.Empty(Directory.EnumerateDirectories(repositoryFiles.WorkspacePath, ".scout-staging-*"));
        Assert.Empty(Directory.EnumerateDirectories(repositoryFiles.WorkspacePath, ".scout-backup-*"));
    }

    [Fact]
    public async Task GenerateAsyncPublishesAQueryableRetrievalIndexAlongsideTheKernel()
    {
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "retrieval kernel source");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var outputPath = Path.Combine(repositoryFiles.WorkspacePath, "retrieval.scout");

        var result = await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion,
            new SqliteRetrievalIndexWriter());

        Assert.Contains("retrieval/index.db", result.ArtifactPaths);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputPath, "manifest.json")));
        var retrieval = manifest.RootElement.GetProperty("retrieval");
        Assert.Equal("retrieval/index.db", retrieval.GetProperty("path").GetString());
        Assert.True(retrieval.GetProperty("cardCount").GetInt32() > 0);

        using var database = SqliteRetrievalDatabase.Open(
            ResolveArtifact(outputPath, "retrieval/index.db"),
            new LexicalEmbeddingModel());
        var hits = database.Search("invoice store", limit: 5);

        Assert.NotEmpty(hits);
        Assert.All(hits, hit => Assert.NotEmpty(hit.Card.Summary));
    }

    [Fact]
    public async Task GenerateAsyncEmitsRoleTemplatesSpecializedAgainstTheKernel()
    {
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "agent kernel source");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var outputPath = Path.Combine(repositoryFiles.WorkspacePath, "agents.scout");

        var result = await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion);

        Assert.Contains("agents/index.json", result.ArtifactPaths);
        foreach (var roleId in new[] { "architect", "developer", "reviewer", "qa" })
        {
            Assert.Contains($"agents/{roleId}.json", result.ArtifactPaths);
        }

        using var reviewer = JsonDocument.Parse(
            await File.ReadAllTextAsync(ResolveArtifact(outputPath, "agents/reviewer.json")));
        var obligations = reviewer.RootElement.GetProperty("obligations")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        // Specialization means the count comes from this kernel, not from a generic reviewer prompt.
        var contractCount = inputs.Intelligence.Contracts.Count.ToString(CultureInfo.InvariantCulture);
        Assert.Contains(obligations, obligation => obligation.Contains(contractCount, StringComparison.Ordinal));

        var handoffTargets = reviewer.RootElement.GetProperty("handoffs")
            .EnumerateArray()
            .Select(handoff => handoff.GetProperty("targetRoleId").GetString()!)
            .ToArray();
        Assert.Equal(["developer", "qa"], handoffTargets);
    }

    [Fact]
    public async Task UpdateAsyncReplacesAnExistingScoutKernelInPlace()
    {
        using var repositoryFiles = TemporaryRepository.CopyFixture(FixtureName, "update kernel source");
        var inputs = CreateInputs(repositoryFiles.RootPath);
        var outputPath = Path.Combine(repositoryFiles.WorkspacePath, "refreshable.scout");

        await ScoutKernelGenerator.GenerateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion);

        var updated = await ScoutKernelGenerator.UpdateAsync(
            inputs.Repository,
            inputs.Structural,
            inputs.Intelligence,
            outputPath,
            ScoutVersion);

        Assert.Equal(outputPath, updated.OutputPath);
        Assert.True(File.Exists(Path.Combine(outputPath, "manifest.json")));
        Assert.Contains("manifest.json", updated.ArtifactPaths);
    }

    private static TestInputs CreateInputs(string repositoryRoot)
    {
        var structural = HandcraftedSnapshotFactory.CreateContractSnapshot();
        var intelligence = BasicIntelligenceCompiler.Compile(
            new ProjectGraph(structural),
            HandcraftedSnapshotFactory.CreatedAtUtc);
        var discoveredFiles = structural.SourceFiles
            .Select(sourceFile => new DiscoveredFile(
                sourceFile.RelativePath,
                0,
                new string('0', 64)))
            .ToArray();
        var repository = new RepositoryDescriptor(
            Path.GetFullPath(repositoryRoot),
            "MinimalCSharpRepository",
            discoveredFiles,
            new string('f', 64),
            new SourceRevision("git", "0123456789abcdef", false),
            []);
        return new TestInputs(repository, structural, intelligence);
    }

    private static List<(string OriginDirectory, string Path)> ReadPublicReferences(
        string outputPath,
        IEnumerable<string> artifactPaths)
    {
        var seen = new HashSet<(string OriginDirectory, string Path)>();
        var references = new List<(string OriginDirectory, string Path)>();

        void Add(string originDirectory, string path)
        {
            if (seen.Add((originDirectory, path)))
            {
                references.Add((originDirectory, path));
            }
        }

        foreach (var markdownPath in artifactPaths.Where(path => path.EndsWith(".md", StringComparison.Ordinal)))
        {
            var content = File.ReadAllText(ResolveArtifact(outputPath, markdownPath));
            var originDirectory = GetReferenceOrigin(markdownPath);
            foreach (Match match in MarkdownReferenceRegex().Matches(content))
            {
                Add(originDirectory, match.Groups["path"].Value);
            }
        }

        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputPath, "manifest.json"))))
        {
            foreach (var entrypoint in manifest.RootElement.GetProperty("entrypoints").EnumerateObject())
            {
                Add(string.Empty, entrypoint.Value.GetString()!);
            }
        }

        foreach (var indexPath in new[] { "agents/index.json", "modules/index.json", "conventions/index.json", "contracts/index.json", "evidence/index.json" })
        {
            using var index = JsonDocument.Parse(File.ReadAllText(ResolveArtifact(outputPath, indexPath)));
            foreach (var item in index.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("detail", out var detail))
                {
                    Add(string.Empty, detail.GetString()!);
                }
            }
        }

        return references;
    }

    // A markdown artifact's own directory is a valid base for a relative link inside it, not just the kernel root.
    private static string GetReferenceOrigin(string artifactPath)
    {
        var slashIndex = artifactPath.LastIndexOf('/');
        return slashIndex < 0 ? string.Empty : artifactPath[..slashIndex];
    }

    private static void AssertReferenceResolves(string outputPath, (string OriginDirectory, string Path) reference)
    {
        Assert.False(Path.IsPathRooted(reference.Path));
        Assert.DoesNotContain('\\', reference.Path);

        var fullRootPath = Path.GetFullPath(outputPath);
        var candidates = reference.OriginDirectory.Length == 0
            ? new[] { reference.Path }
            : new[] { reference.Path, $"{reference.OriginDirectory}/{reference.Path}" };

        string? resolvedPath = null;
        foreach (var candidate in candidates)
        {
            var candidateFullPath = ResolveArtifact(fullRootPath, candidate);
            if (File.Exists(candidateFullPath))
            {
                resolvedPath = candidateFullPath;
                break;
            }
        }

        Assert.True(resolvedPath is not null, $"Kernel reference does not resolve: {reference.Path} (seen under '{reference.OriginDirectory}')");

        var relativePath = Path.GetRelativePath(fullRootPath, resolvedPath!);
        Assert.False(relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string ResolveArtifact(string outputPath, string relativePath)
    {
        return Path.Combine(outputPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsPublicArtifact(string relativePath)
    {
        return !relativePath.StartsWith(".internal/", StringComparison.Ordinal);
    }

    [GeneratedRegex("`(?<path>[^`\\r\\n]+\\.(?:md|json))`", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownReferenceRegex();

    private sealed record TestInputs(
        RepositoryDescriptor Repository,
        StructuralSnapshot Structural,
        IntelligenceSnapshot Intelligence);
}

using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Repository;

public sealed class RepositoryDiscoveryTests
{
    private const string FixtureName = "MinimalCSharpRepository";

    private static readonly string[] ExpectedPaths =
    [
        "Minimal.slnx",
        "src/Billing/Application/Handlers/CancelInvoiceHandler.cs",
        "src/Billing/Application/Handlers/CreateInvoiceHandler.cs",
        "src/Billing/Application/Handlers/GetInvoiceHandler.cs",
        "src/Billing/Application/Handlers/InvoiceHandlerBase.cs",
        "src/Billing/Application/Handlers/UpdateInvoiceHandler.cs",
        "src/Billing/Application/IInvoiceStore.cs",
        "src/Billing/Billing.csproj",
        "src/Billing/Domain/Invoice.cs",
        "src/Billing/Infrastructure/InMemoryInvoiceStore.cs"
    ];

    [Fact]
    public async Task DiscoverAsyncProducesRootIndependentSortedPathsAndFingerprint()
    {
        using var firstRepository = TemporaryRepository.CopyFixture(
            FixtureName,
            "first repository");
        using var secondRepository = TemporaryRepository.CopyFixture(
            FixtureName,
            "second repository",
            reverseFileCreationOrder: true);
        var discovery = new RepositoryDiscovery();

        var first = await discovery.DiscoverAsync(firstRepository.RootPath);
        var second = await discovery.DiscoverAsync(secondRepository.RootPath);

        Assert.NotEqual(first.RootPath, second.RootPath);
        Assert.Equal(first.ContentFingerprint, second.ContentFingerprint);
        Assert.Equal(ExpectedPaths, first.Files.Select(file => file.RelativePath));
        Assert.Equal(ExpectedPaths, second.Files.Select(file => file.RelativePath));
        Assert.Equal(
            first.Files.Select(file => (file.RelativePath, file.Length, file.ContentHash)),
            second.Files.Select(file => (file.RelativePath, file.Length, file.ContentHash)));
        Assert.All(first.Files, file =>
        {
            Assert.False(Path.IsPathRooted(file.RelativePath));
            Assert.DoesNotContain('\\', file.RelativePath);
            Assert.False(file.RelativePath.StartsWith("../", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task DiscoverAsyncExcludesToolBuildAndGeneratedKernelDirectories()
    {
        using var repository = TemporaryRepository.CopyFixture(FixtureName, "excluded directories");
        var excludedPaths = new[]
        {
            ".claude/worktrees/copy/ShouldNotAppear.cs",
            ".git/ShouldNotAppear.cs",
            ".idea/ShouldNotAppear.cs",
            "bin/ShouldNotAppear.cs",
            "generated.scout/ShouldNotAppear.cs",
            "node_modules/package/ShouldNotAppear.cs",
            "src/Billing/OBJ/ShouldNotAppear.cs"
        };
        foreach (var path in excludedPaths)
        {
            repository.WriteFile(path, "internal sealed class ShouldNotAppear;");
        }

        repository.WriteFile(
            "src/Billing/Visible.cs",
            "namespace Billing; internal sealed class Visible;");
        var discovery = new RepositoryDiscovery();

        var result = await discovery.DiscoverAsync(repository.RootPath);

        Assert.Contains(result.Files, file => file.RelativePath == "src/Billing/Visible.cs");
        Assert.DoesNotContain(result.Files, file => file.RelativePath.EndsWith("ShouldNotAppear.cs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverAsyncHandlesSpacesAndUnicodeInRepositoryAndFilePaths()
    {
        using var repository = TemporaryRepository.CopyFixture(FixtureName, "Scout café project");
        const string unicodeRelativePath = "src/Billing/Domain/Crédito Note.cs";
        repository.WriteFile(
            unicodeRelativePath,
            "namespace Billing.Domain; internal sealed class CreditNote;");
        var discovery = new RepositoryDiscovery();

        var result = await discovery.DiscoverAsync(repository.RootPath);

        Assert.Equal("Scout café project", result.Name);
        Assert.Equal(Path.GetFullPath(repository.RootPath), result.RootPath);
        Assert.Contains(result.Files, file => file.RelativePath == unicodeRelativePath);
        Assert.All(result.Files, file => Assert.DoesNotContain('\\', file.RelativePath));
    }

    [Fact]
    public async Task DiscoverAsyncDoesNotInheritAParentGitRevision()
    {
        using var repository = TemporaryRepository.CopyFixture(FixtureName, "nested source repository");
        await repository.InitializeWorkspaceGitRepositoryAsync();
        var discovery = new RepositoryDiscovery();

        var parent = await discovery.DiscoverAsync(repository.WorkspacePath);
        var nested = await discovery.DiscoverAsync(repository.RootPath);

        Assert.Equal("git-commit", parent.Revision.Kind);
        Assert.NotNull(parent.Revision.Value);
        Assert.Equal(SourceRevision.Unknown, nested.Revision);
    }
}

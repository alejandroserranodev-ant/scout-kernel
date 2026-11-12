using Scout.Core.Repository;
using Scout.Tests.Support;

namespace Scout.Tests.Repository;

public sealed class GitIgnoreDiscoveryTests
{
    [Fact]
    public async Task FilesIgnoredByGitAreNotDiscovered()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "gitignore-filter");
        repository.WriteFile(".gitignore", "generated-output/\n");
        repository.WriteFile("generated-output/Bundle.cs", "class Bundle { }");
        await repository.InitializeWorkspaceGitRepositoryAsync().ConfigureAwait(true);

        var descriptor = await new RepositoryDiscovery([".cs"]).DiscoverAsync(repository.RootPath).ConfigureAwait(true);

        Assert.DoesNotContain(descriptor.Files, file => file.RelativePath.StartsWith("generated-output/", StringComparison.Ordinal));
        Assert.Contains(descriptor.Files, file => file.RelativePath.EndsWith("Invoice.cs", StringComparison.Ordinal));
        Assert.Contains(descriptor.Diagnostics, message => message.Contains(".gitignore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OutsideAGitRepositoryNothingIsFiltered()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "gitignore-none");
        repository.WriteFile(".gitignore", "generated-output/\n");
        repository.WriteFile("generated-output/Bundle.cs", "class Bundle { }");

        var descriptor = await new RepositoryDiscovery([".cs"]).DiscoverAsync(repository.RootPath).ConfigureAwait(true);

        Assert.Contains(descriptor.Files, file => file.RelativePath == "generated-output/Bundle.cs");
    }
}

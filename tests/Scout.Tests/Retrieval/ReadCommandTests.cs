using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class ReadCommandTests
{
    private static async Task<(string Kernel, string Repository)> CompileMinimalCSharpKernelAsync(TemporaryRepository repository)
    {
        var kernel = Path.Combine(repository.WorkspacePath, "billing.scout");
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);
        return (kernel, repository.RootPath);
    }

    [Fact]
    public async Task ReadsASymbolByNameFromTheLiveRepository()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "read-symbol");
        var (kernel, repositoryRoot) = await CompileMinimalCSharpKernelAsync(repository).ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ReadCommand
            .RunAsync(["Invoice", "--kernel", kernel, "--repository", repositoryRoot], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("src/Billing/Domain/Invoice.cs", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("sealed record Invoice", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadsAnExplicitFileRangeWithoutTouchingTheIndex()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "read-file-range");
        var (kernel, repositoryRoot) = await CompileMinimalCSharpKernelAsync(repository).ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ReadCommand
            .RunAsync(
                ["src/Billing/Domain/Invoice.cs:1-2", "--kernel", kernel, "--repository", repositoryRoot],
                output,
                error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("namespace Billing.Domain;", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("sealed record Invoice", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailsWithAClearMessageWhenNoSymbolMatches()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "read-missing-symbol");
        var (kernel, repositoryRoot) = await CompileMinimalCSharpKernelAsync(repository).ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ReadCommand
            .RunAsync(["NoSuchSymbolAnywhere", "--kernel", kernel, "--repository", repositoryRoot], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Contains("no symbol named", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlagsASymbolAsStaleWhenItsFileChangedSinceCompile()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "read-stale");
        var (kernel, repositoryRoot) = await CompileMinimalCSharpKernelAsync(repository).ConfigureAwait(true);

        repository.WriteFile(
            "src/Billing/Domain/Invoice.cs",
            "namespace Billing.Domain;\n\n// changed after compile\npublic sealed record Invoice(Guid Id, decimal Amount);\n");

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ReadCommand
            .RunAsync(["Invoice", "--kernel", kernel, "--repository", repositoryRoot], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("stale", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiresAKernelOption()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await ReadCommand.RunAsync(["Invoice"], output, error).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Usage, exitCode);
    }
}

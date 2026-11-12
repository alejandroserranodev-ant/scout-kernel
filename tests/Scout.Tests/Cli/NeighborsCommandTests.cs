using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Cli;

public sealed class NeighborsCommandTests
{
    private static async Task<string> CompileKernelAsync(TemporaryRepository repository, string name)
    {
        var kernel = Path.Combine(repository.WorkspacePath, name);
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);
        return kernel;
    }

    [Fact]
    public async Task ReturnsParseableJsonForAKnownSymbol()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "neighbors-basic");
        var kernel = await CompileKernelAsync(repository, "neighbors.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NeighborsCommand
            .RunAsync(["InvoiceHandlerBase", "--kernel", kernel, "--json", "--depth", "2"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(output.ToString()).RootElement.ValueKind);
    }

    [Fact]
    public async Task ExplainReportsWhetherTwoNamesConnect()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "neighbors-explain");
        var kernel = await CompileKernelAsync(repository, "neighbors-explain.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NeighborsCommand
            .RunAsync(["InvoiceHandlerBase", "--kernel", kernel, "--explain", "NoSuchName", "--json"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.False(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("Connected").GetBoolean());
    }

    [Fact]
    public async Task UnknownSymbolFails()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "neighbors-missing");
        var kernel = await CompileKernelAsync(repository, "neighbors-missing.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NeighborsCommand
            .RunAsync(["NoSuchSymbol", "--kernel", kernel], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Failure, exitCode);
        Assert.Contains("no symbol or contract", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PathAndExplainAreMutuallyExclusive()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await NeighborsCommand
            .RunAsync(["A", "--kernel", "k", "--path", "B", "--explain", "C"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Usage, exitCode);
    }
}

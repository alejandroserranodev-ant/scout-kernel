using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class BriefCommandTests
{
    private static async Task<string> CompileMinimalCSharpKernelAsync(TemporaryRepository repository, string name)
    {
        var kernel = Path.Combine(repository.WorkspacePath, name);
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);
        return kernel;
    }

    [Fact]
    public async Task ReturnsEditSitesWithALineAndNoActiveRules()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "brief-basic");
        var kernel = await CompileMinimalCSharpKernelAsync(repository, "brief.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BriefCommand
            .RunAsync(["invoice", "--kernel", kernel, "--json", "--limit", "5"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        var sites = answer.GetProperty("Sites");
        Assert.True(sites.GetArrayLength() > 0);
        Assert.Equal(
            "This kernel checks no rule mechanically yet.",
            answer.GetProperty("RulesStatus").GetString());
        Assert.Equal(0, answer.GetProperty("Rules").GetArrayLength());
    }

    [Fact]
    public async Task DropsLowerPrioritySitesToFitATightTokenBudget()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "brief-budget");
        var kernel = await CompileMinimalCSharpKernelAsync(repository, "brief-budget.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BriefCommand
            .RunAsync(["invoice", "--kernel", kernel, "--json", "--limit", "8", "--max-tokens", "15"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.True(answer.GetProperty("TruncatedCount").GetInt32() > 0);
        Assert.True(answer.GetProperty("Sites").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task RequiresAKernelOption()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BriefCommand.RunAsync(["a task"], output, error).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Usage, exitCode);
    }
}

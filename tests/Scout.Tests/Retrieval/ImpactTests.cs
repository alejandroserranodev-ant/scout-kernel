using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Cli.Mcp;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class ImpactTests
{
    private static async Task<string> CompileAsync(TemporaryRepository repository, string name)
    {
        var kernel = Path.Combine(repository.WorkspacePath, name);
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);
        return kernel;
    }

    [Fact]
    public async Task ListsEveryDeclarationThatDependsOnTheSymbolWithItsFiles()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-basic");
        var kernel = await CompileAsync(repository, "impact.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        var exit = await ImpactCommand.RunAsync(["InvoiceHandlerBase", "--kernel", kernel, "--json"], output, new StringWriter()).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        var names = answer.GetProperty("Layers")[0].EnumerateArray().Select(node => node.GetProperty("Name").GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["CancelInvoiceHandler", "CreateInvoiceHandler", "GetInvoiceHandler", "UpdateInvoiceHandler"], names);
        Assert.Equal(4, answer.GetProperty("Files").GetArrayLength());
        Assert.All(answer.GetProperty("Layers")[0].EnumerateArray(), node => Assert.Equal("inherits", node.GetProperty("Via").GetString()));
    }

    [Fact]
    public async Task FollowsDependentsTransitivelyUpToTheDepth()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-depth");
        var kernel = await CompileAsync(repository, "impact-depth.scout").ConfigureAwait(true);

        using var shallow = new StringWriter();
        using var deep = new StringWriter();
        await ImpactCommand.RunAsync(["Invoice", "--kernel", kernel, "--json", "--depth", "1"], shallow, new StringWriter()).ConfigureAwait(true);
        await ImpactCommand.RunAsync(["Invoice", "--kernel", kernel, "--json", "--depth", "3"], deep, new StringWriter()).ConfigureAwait(true);

        var shallowTotal = JsonDocument.Parse(shallow.ToString()).RootElement.GetProperty("Total").GetInt32();
        var deepTotal = JsonDocument.Parse(deep.ToString()).RootElement.GetProperty("Total").GetInt32();
        Assert.True(deepTotal >= shallowTotal);
    }

    [Fact]
    public async Task ASymbolNothingDependsOnSaysSoAndNamesTheLimit()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-none");
        var kernel = await CompileAsync(repository, "impact-none.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        await ImpactCommand.RunAsync(["CreateInvoiceHandler", "--kernel", kernel], output, new StringWriter()).ConfigureAwait(true);

        Assert.Contains("no recorded dependents", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("not counted", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLimitCapsTheListedRowsButNotTheTotal()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-limit");
        var kernel = await CompileAsync(repository, "impact-limit.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        await ImpactCommand.RunAsync(["InvoiceHandlerBase", "--kernel", kernel, "--limit", "1"], output, new StringWriter()).ConfigureAwait(true);

        Assert.Contains("4 dependent declaration(s)", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("3 more", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownSymbolFails()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-missing");
        var kernel = await CompileAsync(repository, "impact-missing.scout").ConfigureAwait(true);

        using var error = new StringWriter();
        var exit = await ImpactCommand.RunAsync(["NoSuchSymbol", "--kernel", kernel], new StringWriter(), error).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Failure, exit);
    }

    [Fact]
    public async Task TheNeighborsToolReachesImpactWithoutAFourthTool()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "impact-mcp");
        var kernel = await CompileAsync(repository, "impact-mcp.scout").ConfigureAwait(true);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = "scout_neighbors", arguments = new { symbol = "InvoiceHandlerBase", kernel, impact = true } }
        });

        using var input = new StringReader(request + "\n");
        using var output = new StringWriter();
        await McpServer.RunAsync(input, output, new StringWriter()).ConfigureAwait(true);

        var text = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("CreateInvoiceHandler", text, StringComparison.Ordinal);
        Assert.Contains("\"Total\"", text, StringComparison.Ordinal);
    }
}

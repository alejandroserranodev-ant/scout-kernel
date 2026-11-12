using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Mcp;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Cli;

public sealed class McpServerTests
{
    private const int ExpectedToolCount = 3;

    private static async Task<List<JsonElement>> ExchangeAsync(params string[] requests)
    {
        using var input = new StringReader(string.Join("\n", requests) + "\n");
        using var output = new StringWriter();
        using var error = new StringWriter();
        await McpServer.RunAsync(input, output, error).ConfigureAwait(true);

        return output
            .ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToList();
    }

    [Fact]
    public async Task InitializeReportsProtocolAndToolsCapability()
    {
        var responses = await ExchangeAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""").ConfigureAwait(true);

        var result = Assert.Single(responses).GetProperty("result");
        Assert.False(string.IsNullOrEmpty(result.GetProperty("protocolVersion").GetString()));
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
        Assert.Equal("scout", result.GetProperty("serverInfo").GetProperty("name").GetString());
    }

    [Fact]
    public async Task InitializedNotificationGetsNoReply()
    {
        var responses = await ExchangeAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""").ConfigureAwait(true);

        Assert.Empty(responses);
    }

    [Fact]
    public async Task ToolsListExposesExactlyThreeTools()
    {
        var responses = await ExchangeAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""").ConfigureAwait(true);

        var tools = Assert.Single(responses).GetProperty("result").GetProperty("tools");
        Assert.Equal(ExpectedToolCount, tools.GetArrayLength());
        var names = tools.EnumerateArray().Select(tool => tool.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["scout_brief", "scout_neighbors", "scout_read"], names);
    }

    [Fact]
    public async Task ToolCallRunsTheRealCommandAgainstAKernel()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "mcp-call");
        var kernel = Path.Combine(repository.WorkspacePath, "mcp.scout");
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "scout_brief", arguments = new { task = "invoice", kernel } }
        });
        var responses = await ExchangeAsync(request).ConfigureAwait(true);

        var result = Assert.Single(responses).GetProperty("result");
        Assert.False(result.GetProperty("isError").GetBoolean());
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        Assert.Contains("Sites", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolCallWithMissingKernelReturnsAnErrorResultNotACrash()
    {
        var request = """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"scout_read","arguments":{"target":"X","kernel":"does-not-exist"}}}""";
        var responses = await ExchangeAsync(request).ConfigureAwait(true);

        Assert.True(Assert.Single(responses).GetProperty("result").GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task MalformedJsonYieldsAParseErrorAndTheLoopContinues()
    {
        var responses = await ExchangeAsync(
            "{ this is not json",
            """{"jsonrpc":"2.0","id":5,"method":"tools/list"}""").ConfigureAwait(true);

        Assert.Equal(2, responses.Count);
        Assert.Equal(-32700, responses[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(ExpectedToolCount, responses[1].GetProperty("result").GetProperty("tools").GetArrayLength());
    }

    [Fact]
    public async Task UnknownMethodWithAnIdIsAMethodNotFoundError()
    {
        var responses = await ExchangeAsync("""{"jsonrpc":"2.0","id":6,"method":"nope"}""").ConfigureAwait(true);

        Assert.Equal(-32601, Assert.Single(responses).GetProperty("error").GetProperty("code").GetInt32());
    }
}

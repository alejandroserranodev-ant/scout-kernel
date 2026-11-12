using System.Text.Json;
using System.Text.Json.Nodes;
using Scout.Cli.Commands;

namespace Scout.Cli.Mcp;

/// <summary>
/// A minimal JSON-RPC 2.0 server over stdio implementing exactly the slice of the Model Context Protocol an
/// agent needs to call Scout's three tools: <c>initialize</c>, <c>tools/list</c>, and <c>tools/call</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than built on the official MCP SDK, to keep this process free of a dependency whose
/// compatibility with this project's .NET version was not yet established, and because the protocol surface
/// this needs is small enough to verify by reading it: three methods, three tools.
/// </para>
/// <para>
/// Each tool call is translated into the same argument shape the equivalent CLI command parses, then that
/// command's own <c>RunAsync</c> is called directly — the tool and the CLI command are one tested
/// implementation, not two. A long-lived process answering many calls is itself most of what Phase 3 asked
/// for: process startup and JIT, not the index, dominated single-shot latency (see the README's Phase 2
/// notes), and neither is paid again between calls here.
/// </para>
/// </remarks>
public static class McpServer
{
    private const string ProtocolVersion = "2024-11-05";
    private const string ServerName = "scout";

    internal static readonly ToolDefinition[] Tools =
    [
        new(
            "scout_brief",
            "One call for \"where do I edit\": files/symbols retrieval finds for a task, each with its line, " +
            "the names connected to it, and whether its recorded location is stale.",
            """
            {
              "type": "object",
              "properties": {
                "task": { "type": "string", "description": "The task, in your own words." },
                "kernel": { "type": "string", "description": "Path to the compiled Scout Kernel." },
                "repository": { "type": "string", "description": "Source repository root, if not the kernel path with '.scout' removed." },
                "limit": { "type": "integer", "description": "Maximum edit sites before the token budget trims them." },
                "maxTokens": { "type": "integer", "description": "Approximate token budget (default 600)." }
              },
              "required": ["task", "kernel"]
            }
            """),
        new(
            "scout_read",
            "Returns the exact source lines a symbol or a file:start-end range names, read live from the " +
            "repository (a kernel never stores source bodies).",
            """
            {
              "type": "object",
              "properties": {
                "target": { "type": "string", "description": "A symbol name, or \"<file>:<start>-<end>\"." },
                "kernel": { "type": "string", "description": "Path to the compiled Scout Kernel." },
                "repository": { "type": "string", "description": "Source repository root, if not the kernel path with '.scout' removed." },
                "maxLines": { "type": "integer", "description": "Caps how many lines a single call returns (default 400)." }
              },
              "required": ["target", "kernel"]
            }
            """),
        new(
            "scout_neighbors",
            "Typed, directed relationships (calls, references, renders, inherits, implements) around a declaration. " +
            "Pass 'impact' for what depends on it (the blast radius of changing it), 'path' for the shortest chain to " +
            "another name, or 'explain' for how (or whether) two names connect.",
            """
            {
              "type": "object",
              "properties": {
                "symbol": { "type": "string", "description": "The symbol or contract name to inspect." },
                "kernel": { "type": "string", "description": "Path to the compiled Scout Kernel." },
                "depth": { "type": "integer", "description": "Maximum hops to follow (default 1; 3 with impact)." },
                "impact": { "type": "boolean", "description": "List what depends on the symbol, transitively." },
                "path": { "type": "string", "description": "Target name: find the shortest recorded chain to it." },
                "explain": { "type": "string", "description": "Target name: explain whether/how it connects." }
              },
              "required": ["symbol", "kernel"]
            }
            """)
    ];

    /// <summary>
    /// Reads JSON-RPC requests (one per line) from <paramref name="input"/> until it closes, writing each
    /// response (one per line) to <paramref name="output"/>. Malformed input produces a JSON-RPC error
    /// response rather than stopping the loop, since one bad message should not end the session.
    /// </summary>
    public static async Task RunAsync(
        TextReader input,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? line;
        while ((line = await input.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            string? responseJson;
            try
            {
                responseJson = await HandleLineAsync(line, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                responseJson = JsonRpc.ErrorResponse(id: null, code: -32700, message: $"Parse error: {exception.Message}");
            }

            if (responseJson is not null)
            {
                await output.WriteLineAsync(responseJson).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // Returns null for a notification (no id), which per JSON-RPC 2.0 gets no response at all.
    private static async Task<string?> HandleLineAsync(string line, CancellationToken cancellationToken)
    {
        var request = JsonNode.Parse(line)?.AsObject()
            ?? throw new JsonException("Expected a JSON object.");
        var id = request["id"]?.DeepClone();
        var method = request["method"]?.GetValue<string>();

        return method switch
        {
            "initialize" => JsonRpc.SuccessResponse(id, BuildInitializeResult()),
            "notifications/initialized" => null,
            "tools/list" => JsonRpc.SuccessResponse(id, BuildToolsListResult()),
            "tools/call" => id is null
                ? null
                : JsonRpc.SuccessResponse(id, await HandleToolCallAsync(request["params"], cancellationToken).ConfigureAwait(false)),
            null => JsonRpc.ErrorResponse(id, code: -32600, message: "Invalid request: missing 'method'."),
            _ => id is null ? null : JsonRpc.ErrorResponse(id, code: -32601, message: $"Method not found: {method}")
        };
    }

    private static JsonObject BuildInitializeResult() =>
        new()
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
            ["serverInfo"] = new JsonObject
            {
                ["name"] = ServerName,
                ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "unknown"
            }
        };

    private static JsonObject BuildToolsListResult()
    {
        var tools = new JsonArray();
        foreach (var tool in Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchemaJson)
            });
        }

        return new JsonObject { ["tools"] = tools };
    }

    private static async Task<JsonObject> HandleToolCallAsync(JsonNode? @params, CancellationToken cancellationToken)
    {
        var name = @params?["name"]?.GetValue<string>();
        var arguments = @params?["arguments"]?.AsObject() ?? [];

        var (exitCode, text) = name switch
        {
            "scout_brief" => await ToolDispatch.RunBriefAsync(arguments, cancellationToken).ConfigureAwait(false),
            "scout_read" => await ToolDispatch.RunReadAsync(arguments, cancellationToken).ConfigureAwait(false),
            "scout_neighbors" => await ToolDispatch.RunNeighborsAsync(arguments, cancellationToken).ConfigureAwait(false),
            _ => (ExitCodes.Usage, $"Unknown tool: {name}")
        };

        return new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["isError"] = exitCode != ExitCodes.Success
        };
    }

    internal sealed record ToolDefinition(string Name, string Description, string InputSchemaJson);
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scout.Cli.Mcp;

/// <summary>Builds the two JSON-RPC 2.0 envelope shapes this server ever sends: a result, or an error.</summary>
internal static class JsonRpc
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    internal static string SuccessResponse(JsonNode? id, JsonNode result) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        }.ToJsonString(WriteOptions);

    internal static string ErrorResponse(JsonNode? id, int code, string message) =>
        new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString(WriteOptions);
}

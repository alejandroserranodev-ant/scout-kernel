using System.Globalization;
using System.Text.Json.Nodes;
using Scout.Cli.Commands;

namespace Scout.Cli.Mcp;

/// <summary>
/// Translates an MCP <c>tools/call</c>'s JSON arguments into the argument list the equivalent CLI command
/// already parses, then runs that command. The tool and the CLI command share one implementation.
/// </summary>
internal static class ToolDispatch
{
    internal static Task<(int ExitCode, string Text)> RunBriefAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new List<string> { Require(arguments, "task") };
        AddOption(args, arguments, "kernel", "--kernel", required: true);
        AddOption(args, arguments, "repository", "--repository");
        AddOption(args, arguments, "limit", "--limit");
        AddOption(args, arguments, "maxTokens", "--max-tokens");
        return RunAsync(BriefCommand.RunAsync, args, cancellationToken);
    }

    internal static Task<(int ExitCode, string Text)> RunReadAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new List<string> { Require(arguments, "target") };
        AddOption(args, arguments, "kernel", "--kernel", required: true);
        AddOption(args, arguments, "repository", "--repository");
        AddOption(args, arguments, "maxLines", "--max-lines");
        return RunAsync(ReadCommand.RunAsync, args, cancellationToken);
    }

    internal static Task<(int ExitCode, string Text)> RunNeighborsAsync(JsonObject arguments, CancellationToken cancellationToken)
    {
        var args = new List<string> { Require(arguments, "symbol") };
        AddOption(args, arguments, "kernel", "--kernel", required: true);
        AddOption(args, arguments, "depth", "--depth");
        if (arguments["impact"]?.GetValue<bool>() == true)
        {
            // "What depends on this" is the same question as neighbors, asked backwards; keeping it a mode of
            // scout_neighbors holds the server to its three-tool budget.
            return RunAsync(ImpactCommand.RunAsync, args, cancellationToken);
        }

        AddOption(args, arguments, "path", "--path");
        AddOption(args, arguments, "explain", "--explain");
        return RunAsync(NeighborsCommand.RunAsync, args, cancellationToken);
    }

    private static async Task<(int ExitCode, string Text)> RunAsync(
        Func<IReadOnlyList<string>, TextWriter, TextWriter, CancellationToken, Task<int>> command,
        List<string> args,
        CancellationToken cancellationToken)
    {
        // A tool call always answers in the compact text form plus --json for a caller that wants to parse
        // the fields: --json first, text second would waste a flag an MCP client never needs to pick between.
        args.Add("--json");
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await command(args, output, error, cancellationToken).ConfigureAwait(false);
        return (exitCode, exitCode == ExitCodes.Success ? output.ToString().TrimEnd() : error.ToString().TrimEnd());
    }

    private static string Require(JsonObject arguments, string name) =>
        arguments[name]?.GetValue<string>() ?? throw new ArgumentException($"Missing required argument '{name}'.");

    private static void AddOption(List<string> args, JsonObject arguments, string jsonName, string flag, bool required = false)
    {
        var node = arguments[jsonName];
        if (node is null)
        {
            if (required)
            {
                throw new ArgumentException($"Missing required argument '{jsonName}'.");
            }

            return;
        }

        args.Add(flag);
        args.Add(node.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.Number => node.GetValue<double>().ToString(CultureInfo.InvariantCulture),
            _ => node.GetValue<string>()
        });
    }
}

using Scout.Cli.Commands;
using Scout.Cli.Mcp;

namespace Scout.Cli;

internal static class ScoutCommandLine
{
    private const string Help = """
        Scout compiles a repository into an agent-native Scout Kernel, then answers questions against it.

        Usage:
          scout compile <repository> [--output <path>] [--no-index] [--update] [--embeddings ollama[:<model>]]
          scout update <repository> [--output <path>] [--no-index] [--embeddings ollama[:<model>]]
          scout query "<intent>" --kernel <path> [--limit <n>] [--kind contract|module|symbol|documentation|text|file] [--json] [--no-embeddings] [--profile]
          scout brief "<task>" --kernel <path> [--repository <path>] [--limit <n>] [--max-tokens <n>] [--json] [--format compact]
          scout read "<symbol>"|"<file>:<start>-<end>" --kernel <path> [--repository <path>] [--max-lines <n>] [--json] [--format compact]
          scout neighbors "<symbol>" --kernel <path> [--depth <n>] [--path "<target>"] [--explain "<target>"] [--json] [--format compact]
          scout mcp
          scout db check <repository> [--connection-env <VAR>] [--psql <command>] [--limit <n>] [--json]
          scout install claude --kernel <path> [--project|--user] [--hook-matcher <pattern>] [--dry-run] [--uninstall]
          scout --version
          scout --help

        compile
          The default output is a sibling directory named <repository>.scout.
          A retrieval index is published unless --no-index is given.
          Use --update to refresh an existing kernel in place in CI or scheduled pipelines.
          C#, TypeScript, JavaScript, React, and CSS sources are analyzed.

        --embeddings
          Adds semantic vectors to the retrieval index, so a question can find a card that shares no word with it
          ("whatsapp link" finds waLink). Optional: without it the kernel is lexical and needs no model, no network
          and no service. It uses a local Ollama (default model bge-m3, from OLLAMA_HOST or localhost:11434); the
          compile stops with an error if that model is not available, instead of silently leaving them out.

        update
          Equivalent to compile with --update. It refreshes an existing Scout kernel atomically,
          preserving a valid kernel during the swap and avoiding a stale output in pipelines.

        query
          --kernel accepts the kernel directory or the index file directly.
          Results name a path, what depends on it, and the detail file to open next.
          --json is the form to feed an agent; the default form is for reading.
          A kernel built with --embeddings is queried semantically when its model is available on this machine and
          lexically otherwise, with a notice saying which. --no-embeddings forces the lexical answer.
          --profile prints how long opening the index and searching it took, to stderr.

        brief
          One call for "where do I edit for this task": the files/symbols retrieval finds, each with its line,
          the names connected to it, and whether its recorded location is stale. --max-tokens (default 600)
          drops the lowest-priority sites rather than returning something bigger than asked for. Prints "this
          kernel checks no rule mechanically yet" rather than silently omitting a rules section, since this
          kernel does not check any yet.

        read
          Returns the exact source lines a symbol or a file:start-end range names, reading the live repository —
          a kernel never stores source bodies. --repository defaults to the kernel path with its trailing
          ".scout" removed; pass it explicitly when the kernel was not compiled to that default location.
          Warns when the source file has changed since compile, since the recorded line numbers may no longer
          match. --max-lines caps how much a single call returns (default 400).

        neighbors
          Provisional: walks the reader/read names a card already records (a one-hop signal, not a typed,
          directed edge), so --path and --explain are only ever as precise as that. A real graph is planned
          for a later phase. --depth bounds how many hops any mode will follow (default 1).

        mcp
          Runs Scout as an MCP server over stdio: a minimal, hand-rolled JSON-RPC 2.0 loop exposing exactly
          three tools (scout_brief, scout_read, scout_neighbors) — the same implementation the CLI commands
          above use, so there is one tested behavior, not two. Register it with `claude mcp add scout -- scout
          mcp` or a `.mcp.json` entry of the same shape; see `scout install claude`.

        db check
          Reviews the SQL files' schema declarations (tables, columns, row level security, policies) and, whenever
          a connection is available, compares them with the live database. The connection string is read from an
          environment variable (default SCOUT_DB_URL; change it with --connection-env), passed to the `psql` client,
          and never printed or stored; the session is read-only and reads catalog views, not rows. Without a
          connection (or without psql) it reports the static findings and says why the live check was skipped.

        install claude
          Wires a compiled kernel into Claude Code: a bounded CLAUDE.md section (at most 15 lines, between
          `<!-- scout:begin -->`/`<!-- scout:end -->` markers), a PreToolUse hook that never blocks and prints
          at most 3 lines, and copies of the kernel's generated skills. --project (default) targets the
          current directory; --user targets the home directory's ~/.claude. Idempotent: running it again with
          the same arguments changes nothing further. --dry-run reports what would change without writing.
          --uninstall removes only what a prior install's manifest recorded. `scout update` resyncs the copied
          skills afterward if an install manifest is present.
        """;

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (arguments.Count == 0)
        {
            await error.WriteLineAsync("error: A command is required.").ConfigureAwait(false);
            await output.WriteLineAsync(Help).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        if (arguments.Contains("--help") || arguments.Contains("-h"))
        {
            await output.WriteLineAsync(Help).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (arguments.Count == 1 && arguments[0] == "--version")
        {
            var version = typeof(ScoutCommandLine).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            await output.WriteLineAsync(version).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var rest = arguments.Skip(1).ToArray();
        try
        {
            return arguments[0] switch
            {
                CompileCommand.Verb => await CompileCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                UpdateCommand.Verb => await UpdateCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                QueryCommand.Verb => await QueryCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                BriefCommand.Verb => await BriefCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                ReadCommand.Verb => await ReadCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                NeighborsCommand.Verb => await NeighborsCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                "mcp" => await RunMcpAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                DbCommand.Verb => await DbCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                InstallCommand.Verb => await InstallCommand
                    .RunAsync(rest, output, error, cancellationToken).ConfigureAwait(false),
                var unknown => await UnknownCommandAsync(unknown, error).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("Canceled.").ConfigureAwait(false);
            return ExitCodes.Canceled;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            DirectoryNotFoundException or
            IOException or
            InvalidOperationException)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }
    }

    private static async Task<int> RunMcpAsync(
        string[] arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (arguments.Length > 0)
        {
            await error.WriteLineAsync($"error: 'mcp' takes no arguments (got '{arguments[0]}').").ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        await McpServer.RunAsync(Console.In, output, error, cancellationToken).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"error: Unknown command '{command}'.").ConfigureAwait(false);
        await error.WriteLineAsync("Run 'scout --help' for usage.").ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

using Scout.Cli.Commands;

namespace Scout.Cli;

internal static class ScoutCommandLine
{
    private const string Help = """
        Scout compiles a repository into an agent-native Scout Kernel, then answers questions against it.

        Usage:
          scout compile <repository> [--output <path>] [--no-index] [--update] [--embeddings ollama[:<model>]]
          scout update <repository> [--output <path>] [--no-index] [--embeddings ollama[:<model>]]
          scout query "<intent>" --kernel <path> [--limit <n>] [--kind contract|module|symbol|documentation|text|file] [--json] [--no-embeddings]
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

    private static async Task<int> UnknownCommandAsync(string command, TextWriter error)
    {
        await error.WriteLineAsync($"error: Unknown command '{command}'.").ConfigureAwait(false);
        await error.WriteLineAsync("Run 'scout --help' for usage.").ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

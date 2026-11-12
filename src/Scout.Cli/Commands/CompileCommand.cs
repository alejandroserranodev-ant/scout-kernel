using System.Globalization;
using Scout.Analysis.CSharp;
using Scout.Analysis.Css;
using Scout.Analysis.Markdown;
using Scout.Analysis.Sql;
using Scout.Analysis.TypeScript;
using Scout.Core.Compilation;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Compiles a repository into a Scout Kernel.
/// </summary>
internal static class CompileCommand
{
    internal const string Verb = "compile";

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        string? repositoryPath = null;
        string? outputPath = null;
        var withoutIndex = false;
        string? embeddings = null;
        var update = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--output":
                    if (outputPath is not null)
                    {
                        return await Usage(error, "The --output option can be specified only once.").ConfigureAwait(false);
                    }

                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --output option requires a path.").ConfigureAwait(false);
                    }

                    outputPath = arguments[index];
                    break;

                case "--no-index":
                    withoutIndex = true;
                    break;

                case "--embeddings":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, $"The --embeddings option requires a provider: {EmbeddingProviders.Usage}.").ConfigureAwait(false);
                    }

                    embeddings = arguments[index];
                    break;

                case "--update":
                    update = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (repositoryPath is not null)
                    {
                        return await Usage(error, "Only one repository path can be compiled at a time.").ConfigureAwait(false);
                    }

                    repositoryPath = argument;
                    break;
            }
        }

        if (repositoryPath is null)
        {
            return await Usage(error, "The compile command requires a repository path.").ConfigureAwait(false);
        }

        if (embeddings is not null && withoutIndex)
        {
            return await Usage(error, "The --embeddings option adds vectors to the retrieval index, so it cannot be combined with --no-index.").ConfigureAwait(false);
        }

        var denseModel = embeddings is null ? null : EmbeddingProviders.ForCompile(embeddings, error);
        if (embeddings is not null && denseModel is null)
        {
            return await Usage(error, $"Unknown embeddings provider '{embeddings}'. Use {EmbeddingProviders.Usage}.").ConfigureAwait(false);
        }

        var compiler = new ScoutCompiler(
            [
                new CSharpLanguageAnalyzer(),
                new TypeScriptLanguageAnalyzer(),
                new CssLanguageAnalyzer(),
                new MarkdownLanguageAnalyzer(),
                new SqlLanguageAnalyzer()
            ],
            indexWriter: withoutIndex ? null : new SqliteRetrievalIndexWriter(),
            denseModel: denseModel);

        ScoutCompilationResult result;
        try
        {
            result = await compiler
                .CompileAsync(new ScoutCompileRequest(repositoryPath, outputPath), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DenseEmbeddingUnavailableException exception)
        {
            // The caller asked for semantic vectors. Publishing a kernel without them would hide that the request
            // was not met, so the compile stops and says what is missing.
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var action = update ? "Updated" : "Compiled";
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"{action} '{result.Repository.Name}' to '{result.Kernel.OutputPath}'.")).ConfigureAwait(false);
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Found {result.Structural.Projects.Count} projects, {result.Structural.SourceFiles.Count} source files, " +
            $"{result.Structural.Symbols.Count} symbols, and {result.Structural.Relationships.Count} relationships.")).ConfigureAwait(false);

        if (!withoutIndex)
        {
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"Published a retrieval index; query it with: scout query \"<intent>\" --kernel {result.Kernel.OutputPath}"))
                .ConfigureAwait(false);
        }

        if (denseModel is not null)
        {
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"Added semantic vectors from '{denseModel.ModelId}' ({denseModel.Embedded} embedded, {denseModel.Reused} reused from the cache)."))
                .ConfigureAwait(false);
        }

        if (result.Structural.Diagnostics.Count > 0)
        {
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"Recorded {result.Structural.Diagnostics.Count} analysis diagnostics; inspect the internal IR for incomplete areas."))
                .ConfigureAwait(false);
        }

        return ExitCodes.Success;
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync("Run 'scout --help' for usage.").ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

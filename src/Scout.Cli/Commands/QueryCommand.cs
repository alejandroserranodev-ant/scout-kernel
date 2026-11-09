using System.Globalization;
using System.Text;
using System.Text.Json;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Answers a question against a compiled kernel's retrieval index.
/// </summary>
/// <remarks>
/// This is the command that makes the kernel cheaper than the repository. It returns a handful of cards that
/// each name a path, a consequence, and a detail file, so the agent opens what matters instead of reading to
/// find out what matters.
/// </remarks>
internal static class QueryCommand
{
    internal const string Verb = "query";

    private const string IndexRelativePath = "retrieval/index.db";
    private const int DefaultLimit = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? intent = null;
        string? kernelPath = null;
        string? kind = null;
        var limit = DefaultLimit;
        var asJson = false;
        var lexicalOnly = false;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--kernel":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --kernel option requires a path.").ConfigureAwait(false);
                    }

                    kernelPath = arguments[index];
                    break;

                case "--limit":
                    if (++index >= arguments.Count
                        || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out limit)
                        || limit <= 0)
                    {
                        return await Usage(error, "The --limit option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--kind":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --kind option requires a card kind.").ConfigureAwait(false);
                    }

                    kind = arguments[index];
                    break;

                case "--json":
                    asJson = true;
                    break;

                case "--no-embeddings":
                    lexicalOnly = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (intent is not null)
                    {
                        return await Usage(error, "Only one query can be asked at a time; quote it as a single argument.").ConfigureAwait(false);
                    }

                    intent = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(intent))
        {
            return await Usage(error, "The query command requires a question.").ConfigureAwait(false);
        }

        if (kernelPath is null)
        {
            return await Usage(error, "The query command requires --kernel <path>.").ConfigureAwait(false);
        }

        var indexPath = ResolveIndexPath(kernelPath);
        if (!File.Exists(indexPath))
        {
            await error.WriteLineAsync($"error: no retrieval index at '{indexPath}'. Compile without --no-index first.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        // A kernel built with semantic vectors is queried with the model that built them when this machine has it;
        // otherwise it answers lexically and says so. A machine without any embedding service loses nothing else.
        using var database = SqliteRetrievalDatabase.Open(
            indexPath,
            new LexicalEmbeddingModel(),
            modelId => lexicalOnly ? null : EmbeddingProviders.ForIndex(modelId));
        var result = await database.SearchAsync(intent, limit, kind, cancellationToken).ConfigureAwait(false);

        if (result.Notice is not null && !lexicalOnly)
        {
            await error.WriteLineAsync($"notice: {result.Notice}").ConfigureAwait(false);
        }

        await output.WriteLineAsync(asJson ? RenderJson(intent, result) : RenderText(intent, result.Hits))
            .ConfigureAwait(false);
        return ExitCodes.Success;
    }

    // Accepting either the kernel directory or the database itself removes a guess the caller would otherwise
    // have to make about the kernel's internal layout.
    private static string ResolveIndexPath(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath);
        return Directory.Exists(full) ? Path.Combine(full, IndexRelativePath) : full;
    }

    private static string RenderJson(string intent, RetrievalSearchResult result)
    {
        var hits = result.Hits;
        return JsonSerializer.Serialize(
            new
            {
                Query = intent,
                Count = hits.Count,
                result.Mode,
                result.Notice,
                Hits = hits.Select(hit => new
                {
                    hit.Card.Id,
                    hit.Card.Kind,
                    hit.Card.Name,
                    hit.Card.RelativePath,
                    hit.Card.Summary,
                    hit.Card.Detail,
                    // A card may repeat a facet (a section about two topics has two "topic" values).
                    Facets = hit.Card.Facets
                        .GroupBy(facet => facet.Name, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group => string.Join(", ", group.Select(facet => facet.Value)),
                            StringComparer.Ordinal),
                    hit.Card.Related,
                    Score = Math.Round(hit.Score, 4)
                }).ToArray()
            },
            JsonOptions);
    }

    private static string RenderText(string intent, IReadOnlyList<RetrievalHit> hits)
    {
        if (hits.Count == 0)
        {
            return $"No card matched '{intent}'. The index matches wording, so try the identifiers you expect to exist.";
        }

        var builder = new StringBuilder();
        builder.Append(hits.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" results for '").Append(intent).AppendLine("'.");

        foreach (var hit in hits)
        {
            builder.AppendLine()
                .Append('[').Append(hit.Card.Kind).Append("] ").Append(hit.Card.Name)
                .Append("  (").Append(hit.Score.ToString("0.000", CultureInfo.InvariantCulture)).AppendLine(")");
            if (hit.Card.RelativePath.Length > 0)
            {
                builder.Append("  path:   ").AppendLine(hit.Card.RelativePath);
            }

            builder.Append("  what:   ").AppendLine(hit.Card.Summary);
            if (hit.Card.Related.Count > 0)
            {
                builder.Append("  usedBy: ").AppendLine(string.Join(", ", hit.Card.Related));
            }

            if (hit.Card.Detail is not null)
            {
                builder.Append("  detail: ").AppendLine(hit.Card.Detail);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync("Run 'scout --help' for usage.").ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

using System.Globalization;
using System.Text;
using System.Text.Json;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// <c>scout impact "&lt;symbol&gt;"</c>: what depends on a declaration, directly and transitively — the blast radius
/// of changing it — found by walking the recorded edges backwards. A question grep cannot answer by text alone: it
/// needs the observed calls, references, renders, inherits and implements relationships.
/// </summary>
/// <remarks>
/// Only dependency edges count. <c>contains</c> (a member inside its type) is structure, not a dependency, so a
/// change to a member does not list its enclosing type as dependent. The answer is as complete as the analyzers'
/// relationships are: a dependency an analyzer could not observe is absent, and the output says so.
/// </remarks>
internal static class ImpactCommand
{
    internal const string Verb = "impact";

    private const string IndexRelativePath = "retrieval/index.db";
    private const int DefaultDepth = 3;
    private const int DefaultLimit = 40;
    private const int MaxVisitedNodes = 500;

    // An edge "A calls B" means A depends on B, so the impact of changing B is everything that reaches B backwards.
    private static readonly string[] DependencyKinds = ["calls", "references", "renders", "inherits", "implements"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? symbol = null;
        string? kernelPath = null;
        var depth = DefaultDepth;
        var limit = DefaultLimit;
        var asJson = false;

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

                case "--depth":
                    if (++index >= arguments.Count || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out depth) || depth <= 0)
                    {
                        return await Usage(error, "The --depth option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--limit":
                    if (++index >= arguments.Count || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out limit) || limit <= 0)
                    {
                        return await Usage(error, "The --limit option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--json":
                    asJson = true;
                    break;

                case "--format":
                    if (++index >= arguments.Count || arguments[index] != "compact")
                    {
                        return await Usage(error, "The --format option only accepts 'compact'.").ConfigureAwait(false);
                    }

                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (symbol is not null)
                    {
                        return await Usage(error, "Only one symbol can be analyzed at a time.").ConfigureAwait(false);
                    }

                    symbol = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return await Usage(error, "The impact command requires a symbol name.").ConfigureAwait(false);
        }

        if (kernelPath is null)
        {
            return await Usage(error, "The impact command requires --kernel <path>.").ConfigureAwait(false);
        }

        var full = Path.GetFullPath(kernelPath);
        var indexPath = Directory.Exists(full) ? Path.Combine(full, IndexRelativePath) : full;
        if (!File.Exists(indexPath))
        {
            await error.WriteLineAsync($"error: no retrieval index at '{indexPath}'. Compile without --no-index first.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        using var database = SqliteRetrievalDatabase.Open(indexPath, new LexicalEmbeddingModel());
        if (!NeighborsCommand.TryResolveOne(database, symbol, out var source, out var message))
        {
            await error.WriteLineAsync(message).ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var layers = Walk(database, source!, depth);
        var total = layers.Sum(layer => layer.Count);
        var files = layers.SelectMany(layer => layer)
            .GroupBy(node => node.RelativePath, StringComparer.Ordinal)
            .Select(group => new FileImpact(group.Key, group.Count()))
            .OrderByDescending(file => file.Count)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

        await output.WriteLineAsync(
            asJson
                ? JsonSerializer.Serialize(new { Symbol = source!.Name, Depth = depth, Total = total, Files = files, Layers = layers }, JsonOptions)
                : Render(source!, depth, limit, layers, files, total)).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static List<List<Dependent>> Walk(SqliteRetrievalDatabase database, RetrievalCard source, int depth)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Id };
        var layers = new List<List<Dependent>>();
        var frontier = new List<RetrievalCard> { source };

        for (var hop = 0; hop < depth && visited.Count < MaxVisitedNodes && frontier.Count > 0; hop++)
        {
            var layer = new List<Dependent>();
            var next = new List<RetrievalCard>();
            foreach (var current in frontier)
            {
                foreach (var edge in database.ReadEdges(current.Id, outgoing: false).Where(edge => DependencyKinds.Contains(edge.Kind)))
                {
                    if (visited.Count >= MaxVisitedNodes || !visited.Add(edge.SourceId))
                    {
                        continue;
                    }

                    if (database.FindById(edge.SourceId) is { } dependent)
                    {
                        layer.Add(new Dependent(dependent.Name, dependent.Kind, dependent.RelativePath, LineOf(dependent), edge.Kind, current.Name));
                        next.Add(dependent);
                    }
                }
            }

            if (layer.Count == 0)
            {
                break;
            }

            layers.Add([.. layer.OrderBy(node => node.RelativePath, StringComparer.Ordinal).ThenBy(node => node.Name, StringComparer.Ordinal)]);
            frontier = next;
        }

        return layers;
    }

    private static int? LineOf(RetrievalCard card) =>
        card.Facets.FirstOrDefault(facet => facet.Name == KnownFacetNames.Line).Value is { Length: > 0 } text
        && int.TryParse(text, CultureInfo.InvariantCulture, out var line)
            ? line
            : null;

    private sealed record Dependent(string Name, string Kind, string RelativePath, int? Line, string Via, string From);

    private sealed record FileImpact(string Path, int Count);

    private static string Render(RetrievalCard source, int depth, int limit, List<List<Dependent>> layers, FileImpact[] files, int total)
    {
        var builder = new StringBuilder();
        if (total == 0)
        {
            return $"impact of {source.Name}: no recorded dependents within {depth} hop(s). " +
                "Dependencies an analyzer could not observe (reflection, dynamic calls, other languages) are not counted.";
        }

        builder.Append("impact of ").Append(source.Name).Append(": ").Append(total).Append(" dependent declaration(s) in ")
            .Append(files.Length).Append(" file(s), within ").Append(depth).AppendLine(" hop(s)");

        var shown = 0;
        for (var hop = 0; hop < layers.Count && shown < limit; hop++)
        {
            builder.Append("hop ").Append(hop + 1).AppendLine(":");
            foreach (var node in layers[hop])
            {
                if (shown >= limit)
                {
                    break;
                }

                shown++;

                builder.Append("  ").Append(node.Name).Append("  ").Append(node.RelativePath);
                if (node.Line is { } line)
                {
                    builder.Append(':').Append(line);
                }

                builder.Append("  (").Append(node.Via).Append(' ').Append(hop == 0 ? source.Name : node.From).AppendLine(")");
            }
        }

        if (total > shown)
        {
            builder.Append("  ... ").Append(total - shown).AppendLine(" more (raise --limit).");
        }

        builder.Append("files: ").Append(string.Join(", ", files.Take(limit).Select(file => $"{file.Path} ({file.Count})")));
        return builder.ToString().TrimEnd();
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync("usage: scout impact \"<symbol>\" --kernel <path> [--depth <n>] [--limit <n>] [--json] [--format compact]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

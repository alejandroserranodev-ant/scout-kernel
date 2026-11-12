using System.Globalization;
using System.Text;
using System.Text.Json;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Walks the names a card already records as connected to it (readers and reads, folded into
/// <see cref="RetrievalCard.Related"/> at compile time) to answer "what is near this symbol", "is there a path
/// to that one", and "why are these two connected".
/// </summary>
/// <remarks>
/// This is a provisional implementation. It can only see what a card's own <c>Related</c> names capture — a
/// one-hop reader/read relationship, not a typed, directed edge — so a path longer than one hop, or an
/// "explain" answer, is only ever as precise as that. A real graph (<c>edges</c> table, typed and directed) is
/// planned for a later phase; this trades precision for being usable today, without reading the much larger
/// structural IR per call.
/// </remarks>
internal static class NeighborsCommand
{
    internal const string Verb = "neighbors";

    private const string IndexRelativePath = "retrieval/index.db";
    private const int DefaultDepth = 1;
    private const int MaxVisitedNodes = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? symbol = null;
        string? kernelPath = null;
        string? pathTarget = null;
        string? explainTarget = null;
        var depth = DefaultDepth;
        var asJson = false;
        var compact = false;

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
                    if (++index >= arguments.Count
                        || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out depth)
                        || depth <= 0)
                    {
                        return await Usage(error, "The --depth option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--path":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --path option requires a target symbol name.").ConfigureAwait(false);
                    }

                    pathTarget = arguments[index];
                    break;

                case "--explain":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --explain option requires a target symbol name.").ConfigureAwait(false);
                    }

                    explainTarget = arguments[index];
                    break;

                case "--json":
                    asJson = true;
                    break;

                case "--format":
                    if (++index >= arguments.Count || arguments[index] != "compact")
                    {
                        return await Usage(error, "The --format option only accepts 'compact'.").ConfigureAwait(false);
                    }

                    compact = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (symbol is not null)
                    {
                        return await Usage(error, "Only one symbol can be inspected at a time.").ConfigureAwait(false);
                    }

                    symbol = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return await Usage(error, "The neighbors command requires a symbol name.").ConfigureAwait(false);
        }

        if (kernelPath is null)
        {
            return await Usage(error, "The neighbors command requires --kernel <path>.").ConfigureAwait(false);
        }

        if (pathTarget is not null && explainTarget is not null)
        {
            return await Usage(error, "Pass only one of --path or --explain.").ConfigureAwait(false);
        }

        var indexPath = ResolveIndexPath(kernelPath);
        if (!File.Exists(indexPath))
        {
            await error.WriteLineAsync($"error: no retrieval index at '{indexPath}'. Compile without --no-index first.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        using var database = SqliteRetrievalDatabase.Open(indexPath, new LexicalEmbeddingModel());
        if (!TryResolveOne(database, symbol, out var sourceCard, out var ambiguityOrMissing))
        {
            await error.WriteLineAsync(ambiguityOrMissing).ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        if (explainTarget is not null)
        {
            var explanation = Explain(database, sourceCard!, explainTarget, depth);
            await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(explanation, JsonOptions) : RenderExplain(explanation))
                .ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (pathTarget is not null)
        {
            var path = FindPath(database, sourceCard!, pathTarget, depth);
            await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(path, JsonOptions) : RenderPath(path))
                .ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var layers = Expand(database, sourceCard!, depth);
        await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(layers, JsonOptions) : RenderLayers(sourceCard!, layers, compact))
            .ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static bool TryResolveOne(SqliteRetrievalDatabase database, string name, out RetrievalCard? card, out string message)
    {
        var matches = database.FindByExactName(KnownCardKinds.Symbol, name);
        if (matches.Count == 0)
        {
            matches = database.FindByExactName(KnownCardKinds.Contract, name);
        }

        if (matches.Count == 0)
        {
            card = null;
            message = $"error: no symbol or contract named '{name}' in this kernel.";
            return false;
        }

        if (matches.Count > 1)
        {
            card = null;
            message = $"error: '{name}' names {matches.Count} cards; neighbors needs a unique name.";
            return false;
        }

        card = matches[0];
        message = string.Empty;
        return true;
    }

    // One-hop neighbours are exactly a card's own Related names, resolved back to cards where possible, so a
    // caller sees what they are (kind, path) and not just a bare name that might not even resolve.
    private static List<List<NeighbourNode>> Expand(SqliteRetrievalDatabase database, RetrievalCard source, int depth)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Name };
        var layers = new List<List<NeighbourNode>>();
        var frontier = new List<RetrievalCard> { source };

        for (var hop = 0; hop < depth && visited.Count < MaxVisitedNodes; hop++)
        {
            var nextLayer = new List<NeighbourNode>();
            var nextFrontier = new List<RetrievalCard>();
            foreach (var current in frontier)
            {
                foreach (var name in current.Related)
                {
                    if (!visited.Add(name) || visited.Count > MaxVisitedNodes)
                    {
                        continue;
                    }

                    var resolved = ResolveByName(database, name);
                    nextLayer.Add(new NeighbourNode(name, resolved?.Kind, resolved?.RelativePath));
                    if (resolved is not null)
                    {
                        nextFrontier.Add(resolved);
                    }
                }
            }

            if (nextLayer.Count == 0)
            {
                break;
            }

            layers.Add(nextLayer);
            frontier = nextFrontier;
        }

        return layers;
    }

    private static PathResult FindPath(SqliteRetrievalDatabase database, RetrievalCard source, string targetName, int maxDepth)
    {
        if (string.Equals(source.Name, targetName, StringComparison.OrdinalIgnoreCase))
        {
            return new PathResult([source.Name], Found: true, maxDepth);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Name };
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var queue = new Queue<(RetrievalCard Card, int Depth)>();
        queue.Enqueue((source, 0));

        while (queue.Count > 0 && visited.Count < MaxVisitedNodes)
        {
            var (current, currentDepth) = queue.Dequeue();
            if (currentDepth >= maxDepth)
            {
                continue;
            }

            foreach (var name in current.Related)
            {
                if (!visited.Add(name))
                {
                    continue;
                }

                parent[name] = current.Name;
                if (string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    var chain = new List<string> { name };
                    var cursor = current.Name;
                    while (!string.Equals(cursor, source.Name, StringComparison.Ordinal))
                    {
                        chain.Add(cursor);
                        cursor = parent[cursor];
                    }

                    chain.Add(source.Name);
                    chain.Reverse();
                    return new PathResult(chain, Found: true, maxDepth);
                }

                if (ResolveByName(database, name) is { } resolved)
                {
                    queue.Enqueue((resolved, currentDepth + 1));
                }
            }
        }

        return new PathResult([], Found: false, maxDepth);
    }

    private static ExplainResult Explain(SqliteRetrievalDatabase database, RetrievalCard source, string targetName, int maxDepth)
    {
        var direct = source.Related.Any(name => string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase));
        if (direct)
        {
            return new ExplainResult(
                source.Name,
                targetName,
                Connected: true,
                Hops: 1,
                "Named in each other's recorded reader/read relationship. This kernel does not yet record which " +
                "direction or kind (calls, references, renders) — only that a connection was observed.");
        }

        var path = FindPath(database, source, targetName, maxDepth);
        return path.Found
            ? new ExplainResult(
                source.Name,
                targetName,
                Connected: true,
                path.Chain.Count - 1,
                $"Reachable only through {path.Chain.Count - 2} intermediate name(s); precision drops with distance, " +
                "since each hop is itself an unqualified reader/read relationship.")
            : new ExplainResult(
                source.Name,
                targetName,
                Connected: false,
                Hops: null,
                $"No recorded reader/read relationship connects them within {maxDepth} hop(s).");
    }

    private static RetrievalCard? ResolveByName(SqliteRetrievalDatabase database, string name)
    {
        var matches = database.FindByExactName(KnownCardKinds.Symbol, name);
        if (matches.Count == 1)
        {
            return matches[0];
        }

        matches = database.FindByExactName(KnownCardKinds.Contract, name);
        return matches.Count == 1 ? matches[0] : null;
    }

    private sealed record NeighbourNode(string Name, string? Kind, string? RelativePath);

    private sealed record PathResult(IReadOnlyList<string> Chain, bool Found, int MaxDepth);

    private sealed record ExplainResult(string Source, string Target, bool Connected, int? Hops, string Note);

    private static string RenderLayers(RetrievalCard source, List<List<NeighbourNode>> layers, bool compact)
    {
        var builder = new StringBuilder();
        builder.Append("neighbors of ").AppendLine(source.Name);
        if (layers.Count == 0)
        {
            builder.Append("(none recorded)");
            return builder.ToString();
        }

        for (var hop = 0; hop < layers.Count; hop++)
        {
            if (!compact)
            {
                builder.AppendLine();
            }

            builder.Append("hop ").Append(hop + 1).AppendLine(":");
            foreach (var node in layers[hop])
            {
                builder.Append("  ").Append(node.Name);
                if (node.Kind is not null)
                {
                    builder.Append("  [").Append(node.Kind).Append("] ").Append(node.RelativePath);
                }
                else
                {
                    builder.Append("  (name only; does not resolve to a card)");
                }

                builder.AppendLine();
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderPath(PathResult result) =>
        result.Found
            ? string.Join(" -> ", result.Chain)
            : $"no path found within {result.MaxDepth} hop(s)";

    private static string RenderExplain(ExplainResult result) =>
        result.Connected
            ? $"{result.Source} -> {result.Target}: connected ({result.Hops} hop(s)). {result.Note}"
            : $"{result.Source} -> {result.Target}: not connected. {result.Note}";

    private static string ResolveIndexPath(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath);
        return Directory.Exists(full) ? Path.Combine(full, IndexRelativePath) : full;
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout neighbors \"<symbol>\" --kernel <path> [--depth <n>] [--path \"<target>\"] [--explain \"<target>\"] [--json] [--format compact]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

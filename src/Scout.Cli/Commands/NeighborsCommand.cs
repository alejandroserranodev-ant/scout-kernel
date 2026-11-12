using System.Globalization;
using System.Text;
using System.Text.Json;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Walks the typed, directed edges a kernel recorded between declarations (calls, references, renders, inherits,
/// implements, contains) to answer "what is near this symbol", "is there a path to that one", and "why are these
/// two connected".
/// </summary>
/// <remarks>
/// Every hop is a relationship an analyzer observed, with its kind and its direction, so an answer says
/// <em>how</em> two names connect and not merely that they do. Paths are searched ignoring direction (a caller
/// and a callee are both "connected"), and each step reports the direction it was actually recorded in.
/// </remarks>
internal static class NeighborsCommand
{
    internal const string Verb = "neighbors";

    private const string IndexRelativePath = "retrieval/index.db";
    private const int DefaultDepth = 1;
    private const int MaxVisitedNodes = 200;
    private const string Outgoing = "out";
    private const string Incoming = "in";

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
        if (!TryResolveOne(database, symbol, out var sourceCard, out var message))
        {
            await error.WriteLineAsync(message).ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var target = explainTarget ?? pathTarget;
        if (target is null)
        {
            var layers = Expand(database, sourceCard!, depth);
            await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(layers, JsonOptions) : RenderLayers(sourceCard!, layers, compact))
                .ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (!TryResolveOne(database, target, out var targetCard, out message))
        {
            // A target that names nothing is an answer to "are they connected", not a usage error.
            if (explainTarget is not null)
            {
                var missing = new ExplainResult(sourceCard!.Name, target, Connected: false, Hops: null, [], message.Replace("error: ", string.Empty, StringComparison.Ordinal));
                await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(missing, JsonOptions) : RenderExplain(missing)).ConfigureAwait(false);
                return ExitCodes.Success;
            }

            await error.WriteLineAsync(message).ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var steps = FindPath(database, sourceCard!, targetCard!, depth);
        if (explainTarget is not null)
        {
            var explanation = Explain(sourceCard!, targetCard!, steps, depth);
            await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(explanation, JsonOptions) : RenderExplain(explanation))
                .ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var path = new PathResult(steps is not null, steps ?? [], depth);
        await output.WriteLineAsync(asJson ? JsonSerializer.Serialize(path, JsonOptions) : RenderPath(sourceCard!, path))
            .ConfigureAwait(false);
        return ExitCodes.Success;
    }

    internal static bool TryResolveOne(SqliteRetrievalDatabase database, string name, out RetrievalCard? card, out string message)
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

    // Every card one edge away, with the edge's kind and the direction it was recorded in, in a stable order.
    private static List<Hop> Adjacent(SqliteRetrievalDatabase database, RetrievalCard card)
    {
        var hops = new List<Hop>();
        foreach (var outgoing in new[] { true, false })
        {
            foreach (var edge in database.ReadEdges(card.Id, outgoing))
            {
                var neighbour = database.FindById(outgoing ? edge.TargetId : edge.SourceId);
                if (neighbour is not null)
                {
                    hops.Add(new Hop(neighbour, edge.Kind, outgoing ? Outgoing : Incoming));
                }
            }
        }

        return hops
            .OrderBy(hop => hop.Card.Name, StringComparer.Ordinal)
            .ThenBy(hop => hop.Kind, StringComparer.Ordinal)
            .ThenBy(hop => hop.Direction, StringComparer.Ordinal)
            .ThenBy(hop => hop.Card.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static List<List<NeighbourNode>> Expand(SqliteRetrievalDatabase database, RetrievalCard source, int depth)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Id };
        var layers = new List<List<NeighbourNode>>();
        var frontier = new List<RetrievalCard> { source };

        for (var hop = 0; hop < depth && visited.Count < MaxVisitedNodes; hop++)
        {
            var nextLayer = new List<NeighbourNode>();
            var nextFrontier = new List<RetrievalCard>();
            foreach (var current in frontier)
            {
                foreach (var adjacent in Adjacent(database, current))
                {
                    if (visited.Count >= MaxVisitedNodes || !visited.Add(adjacent.Card.Id))
                    {
                        continue;
                    }

                    nextLayer.Add(new NeighbourNode(
                        adjacent.Card.Name, adjacent.Card.Kind, adjacent.Card.RelativePath, adjacent.Kind, adjacent.Direction, current.Name));
                    nextFrontier.Add(adjacent.Card);
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

    // Breadth-first over edges in either direction; returns the steps of a shortest chain, or null when none exists
    // within the depth. An empty list means the two are the same card.
    private static List<PathStep>? FindPath(SqliteRetrievalDatabase database, RetrievalCard source, RetrievalCard target, int maxDepth)
    {
        if (source.Id == target.Id)
        {
            return [];
        }

        var parent = new Dictionary<string, (RetrievalCard From, Hop Via)>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { source.Id };
        var queue = new Queue<(RetrievalCard Card, int Depth)>();
        queue.Enqueue((source, 0));

        while (queue.Count > 0 && visited.Count < MaxVisitedNodes)
        {
            var (current, currentDepth) = queue.Dequeue();
            if (currentDepth >= maxDepth)
            {
                continue;
            }

            foreach (var adjacent in Adjacent(database, current))
            {
                if (!visited.Add(adjacent.Card.Id))
                {
                    continue;
                }

                parent[adjacent.Card.Id] = (current, adjacent);
                if (adjacent.Card.Id == target.Id)
                {
                    var steps = new List<PathStep>();
                    var cursor = adjacent.Card.Id;
                    while (cursor != source.Id)
                    {
                        var (from, via) = parent[cursor];
                        steps.Add(new PathStep(from.Name, via.Card.Name, via.Kind, via.Direction));
                        cursor = from.Id;
                    }

                    steps.Reverse();
                    return steps;
                }

                queue.Enqueue((adjacent.Card, currentDepth + 1));
            }
        }

        return null;
    }

    private static ExplainResult Explain(RetrievalCard source, RetrievalCard target, List<PathStep>? steps, int maxDepth) =>
        steps is null
            ? new ExplainResult(
                source.Name, target.Name, Connected: false, Hops: null, [],
                $"No recorded calls, references, renders, inherits, implements, or contains relationship connects them within {maxDepth} hop(s).")
            : new ExplainResult(
                source.Name, target.Name, Connected: true, steps.Count, steps,
                steps.Count == 0
                    ? "They are the same declaration."
                    : steps.Count == 1
                        ? "Directly related; the step shows the observed relationship and its direction."
                        : $"Connected through {steps.Count - 1} intermediate declaration(s); each step is an observed relationship.");

    private sealed record Hop(RetrievalCard Card, string Kind, string Direction);

    private sealed record NeighbourNode(string Name, string Kind, string RelativePath, string Via, string Direction, string From);

    private sealed record PathStep(string From, string To, string Kind, string Direction);

    private sealed record PathResult(bool Found, IReadOnlyList<PathStep> Steps, int MaxDepth);

    private sealed record ExplainResult(string Source, string Target, bool Connected, int? Hops, IReadOnlyList<PathStep> Steps, string Note);

    private static string Describe(string kind, string direction, string name) =>
        direction == Outgoing ? $"{kind} -> {name}" : $"{kind} <- {name}";

    private static string RenderLayers(RetrievalCard source, List<List<NeighbourNode>> layers, bool compact)
    {
        var builder = new StringBuilder();
        builder.Append("neighbors of ").AppendLine(source.Name);
        if (layers.Count == 0)
        {
            builder.Append("(no recorded relationships)");
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
                builder.Append("  ").Append(Describe(node.Via, node.Direction, node.Name));
                if (hop > 0)
                {
                    builder.Append(" (via ").Append(node.From).Append(')');
                }

                builder.Append("  [").Append(node.Kind).Append("] ").AppendLine(node.RelativePath);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string RenderSteps(string start, IReadOnlyList<PathStep> steps)
    {
        var builder = new StringBuilder(start);
        foreach (var step in steps)
        {
            builder.Append(step.Direction == Outgoing ? $" --{step.Kind}--> " : $" <--{step.Kind}-- ").Append(step.To);
        }

        return builder.ToString();
    }

    private static string RenderPath(RetrievalCard source, PathResult result) =>
        result.Found
            ? RenderSteps(source.Name, result.Steps)
            : $"no path found within {result.MaxDepth} hop(s)";

    private static string RenderExplain(ExplainResult result) =>
        result.Connected
            ? $"{RenderSteps(result.Source, result.Steps)}: connected ({result.Hops} hop(s)). {result.Note}"
            : $"{result.Source} / {result.Target}: not connected. {result.Note}";

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

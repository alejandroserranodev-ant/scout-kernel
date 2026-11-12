using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Scout.Cli.Rules;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Answers "where do I edit for this task" in one call: files and symbols with their line, the names connected
/// to each, and whether the recorded location is still trustworthy — instead of the several grep/read round
/// trips an agent would otherwise need to reach the same answer.
/// </summary>
/// <remarks>
/// A brief includes only the active rules whose scope touches the files it names (at most
/// <see cref="MaxRulesInBrief"/>, each with a shortened quote), and always says what the rules section means —
/// none defined, none applicable, or some — so a caller never reads an omission as "no rules apply."
/// </remarks>
internal static class BriefCommand
{
    internal const string Verb = "brief";

    private const string IndexRelativePath = "retrieval/index.db";
    private const string KernelDirectorySuffix = ".scout";
    private const int DefaultLimit = 5;
    private const int DefaultMaxTokens = 600;
    private const int CharsPerTokenApprox = 4;
    private const int MaxNeighboursPerSite = 6;
    private const int MaxRulesInBrief = 5;
    private const int MaxRuleQuoteChars = 160;
    private const string NoRulesDefined = "No active rules are defined for this repository.";
    private const string NoRuleApplies = "No active rule applies to these files.";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? task = null;
        string? kernelPath = null;
        string? repositoryPath = null;
        var limit = DefaultLimit;
        var maxTokens = DefaultMaxTokens;
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

                case "--repository":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --repository option requires a path.").ConfigureAwait(false);
                    }

                    repositoryPath = arguments[index];
                    break;

                case "--limit":
                    if (++index >= arguments.Count
                        || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out limit)
                        || limit <= 0)
                    {
                        return await Usage(error, "The --limit option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--max-tokens":
                    if (++index >= arguments.Count
                        || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out maxTokens)
                        || maxTokens <= 0)
                    {
                        return await Usage(error, "The --max-tokens option requires a positive integer.").ConfigureAwait(false);
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

                    compact = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (task is not null)
                    {
                        return await Usage(error, "Only one task can be briefed at a time; quote it as a single argument.").ConfigureAwait(false);
                    }

                    task = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(task))
        {
            return await Usage(error, "The brief command requires a task, in your own words.").ConfigureAwait(false);
        }

        if (kernelPath is null)
        {
            return await Usage(error, "The brief command requires --kernel <path>.").ConfigureAwait(false);
        }

        var indexPath = ResolveIndexPath(kernelPath);
        if (!File.Exists(indexPath))
        {
            await error.WriteLineAsync($"error: no retrieval index at '{indexPath}'. Compile without --no-index first.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var repositoryRoot = repositoryPath ?? InferRepositoryRoot(kernelPath);

        using var database = SqliteRetrievalDatabase.Open(
            indexPath, new LexicalEmbeddingModel(), _ => null);
        var result = await database.SearchAsync(task, limit, kind: null, cancellationToken).ConfigureAwait(false);

        var sites = result.Hits.Select(hit => BuildSite(hit, repositoryRoot)).ToList();
        var budgetChars = maxTokens * CharsPerTokenApprox;
        var rules = FindApplicableRules(repositoryRoot, sites, out var rulesStatus);
        var truncatedCount = ApplyBudget(sites, rules, rulesStatus, budgetChars, compact);

        await output.WriteLineAsync(
            asJson
                ? RenderJson(task, sites, truncatedCount, rules, rulesStatus)
                : RenderText(task, sites, truncatedCount, compact, rules, rulesStatus))
            .ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private sealed record BriefRule(string Id, string Modality, string Quote, string Source);

    private static BriefRule[] FindApplicableRules(string? repositoryRoot, IReadOnlyList<EditSite> sites, out string status)
    {
        if (repositoryRoot is null)
        {
            status = NoRulesDefined;
            return [];
        }

        IReadOnlyList<RuleDefinition> all;
        try
        {
            all = RuleStore.LoadAll(repositoryRoot);
        }
        catch (RuleSchemaException exception)
        {
            status = $"Rules could not be read: {exception.Message}";
            return [];
        }

        var active = all.Where(rule => rule.State == RuleVocabulary.Active).ToArray();
        if (active.Length == 0)
        {
            status = NoRulesDefined;
            return [];
        }

        var paths = sites.Select(site => site.RelativePath).Where(path => path.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var applicable = active
            .Where(rule => paths.Any(path => RuleChecker.AppliesTo(rule, path)))
            .Take(MaxRulesInBrief)
            .Select(rule => new BriefRule(rule.Id, rule.Modality, Shorten(rule.Quote.Text), rule.Quote.Source))
            .ToArray();
        status = applicable.Length == 0
            ? NoRuleApplies
            : $"{applicable.Length} active rule(s) apply; `scout check` verifies them.";
        return applicable;
    }

    private static string Shorten(string text) =>
        text.Length <= MaxRuleQuoteChars ? text : text[..MaxRuleQuoteChars].TrimEnd() + "...";

    private sealed record EditSite(
        string Kind,
        string Name,
        string RelativePath,
        int? Line,
        string Summary,
        IReadOnlyList<string> Neighbours,
        bool Stale);

    private static EditSite BuildSite(RetrievalHit hit, string? repositoryRoot)
    {
        var card = hit.Card;
        var lineText = card.Facets.FirstOrDefault(facet => facet.Name == KnownFacetNames.Line).Value;
        var line = lineText is { Length: > 0 } && int.TryParse(lineText, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (int?)null;

        return new EditSite(
            card.Kind,
            card.Name,
            card.RelativePath,
            line,
            card.Summary,
            card.Related.Take(MaxNeighboursPerSite).ToArray(),
            IsStale(card, repositoryRoot));
    }

    private static bool IsStale(RetrievalCard card, string? repositoryRoot)
    {
        if (repositoryRoot is null || card.RelativePath.Length == 0)
        {
            return false;
        }

        var hash = card.Facets.FirstOrDefault(facet => facet.Name == KnownFacetNames.ContentHash).Value;
        var algorithm = card.Facets.FirstOrDefault(facet => facet.Name == KnownFacetNames.HashAlgorithm).Value;
        if (string.IsNullOrEmpty(hash) || !string.Equals(algorithm, "sha256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var absolutePath = Path.Combine(repositoryRoot, card.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            return false;
        }

        var liveHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(absolutePath)));
        return !string.Equals(liveHash, hash, StringComparison.OrdinalIgnoreCase);
    }

    // Drops the least important sites first until the rendered brief fits the caller's token budget, rather
    // than silently handing back something larger than asked for.
    private static int ApplyBudget(List<EditSite> sites, IReadOnlyList<BriefRule> rules, string rulesStatus, int budgetChars, bool compact)
    {
        var truncated = 0;
        while (sites.Count > 1 && EstimateChars(sites, rules, rulesStatus, compact) > budgetChars)
        {
            sites.RemoveAt(sites.Count - 1);
            truncated++;
        }

        return truncated;
    }

    private static int EstimateChars(IReadOnlyList<EditSite> sites, IReadOnlyList<BriefRule> rules, string rulesStatus, bool compact) =>
        RenderText("", sites, 0, compact, rules, rulesStatus).Length;

    private static string? InferRepositoryRoot(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath).TrimEnd(Path.DirectorySeparatorChar);
        return full.EndsWith(KernelDirectorySuffix, StringComparison.OrdinalIgnoreCase)
            ? full[..^KernelDirectorySuffix.Length]
            : null;
    }

    private static string ResolveIndexPath(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath);
        return Directory.Exists(full) ? Path.Combine(full, IndexRelativePath) : full;
    }

    private static string RenderJson(
        string task, IReadOnlyList<EditSite> sites, int truncatedCount, IReadOnlyList<BriefRule> rules, string rulesStatus) =>
        JsonSerializer.Serialize(
            new
            {
                Task = task,
                Sites = sites.Select(site => new
                {
                    site.Kind,
                    site.Name,
                    site.RelativePath,
                    site.Line,
                    site.Summary,
                    site.Neighbours,
                    site.Stale
                }),
                TruncatedCount = truncatedCount,
                Rules = rules,
                RulesStatus = rulesStatus
            },
            JsonOptions);

    private static string RenderText(
        string task, IReadOnlyList<EditSite> sites, int truncatedCount, bool compact, IReadOnlyList<BriefRule> rules, string rulesStatus)
    {
        var builder = new StringBuilder();
        if (task.Length > 0)
        {
            builder.Append("brief: ").AppendLine(task);
        }

        if (sites.Count == 0)
        {
            builder.Append("No edit site matched. The index matches wording, so try the identifiers you expect to exist.");
            return builder.ToString().TrimEnd();
        }

        foreach (var site in sites)
        {
            if (!compact)
            {
                builder.AppendLine();
            }

            builder.Append('[').Append(site.Kind).Append("] ").Append(site.Name);
            if (site.Line is { } line)
            {
                builder.Append(" at ").Append(site.RelativePath).Append(':').Append(line);
            }
            else if (site.RelativePath.Length > 0)
            {
                builder.Append(" at ").Append(site.RelativePath);
            }

            if (site.Stale)
            {
                builder.Append("  [stale: source changed since compile]");
            }

            builder.AppendLine();
            builder.Append("  what: ").AppendLine(site.Summary);
            if (site.Neighbours.Count > 0)
            {
                builder.Append("  near: ").AppendLine(string.Join(", ", site.Neighbours));
            }
        }

        if (truncatedCount > 0)
        {
            builder.AppendLine().Append(truncatedCount).Append(" lower-priority site(s) dropped to fit the token budget; raise --max-tokens to see them.").AppendLine();
        }

        builder.AppendLine().Append("rules: ").AppendLine(rulesStatus);
        foreach (var rule in rules)
        {
            builder.Append("  ").Append(rule.Id).Append(" (").Append(rule.Modality).Append("): \"").Append(rule.Quote).Append("\" [").Append(rule.Source).AppendLine("]");
        }

        return builder.ToString().TrimEnd();
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout brief \"<task>\" --kernel <path> [--repository <path>] [--limit <n>] [--max-tokens <n>] [--json] [--format compact]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

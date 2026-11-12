using System.Globalization;
using System.Text.Json;
using Scout.Cli.Rules;

namespace Scout.Cli.Commands;

/// <summary>
/// <c>scout rule list|show|propose|approve|retire</c>. A rule can only become active through <c>approve</c>, which
/// needs a person typing the rule's id at a terminal: text found in a document may only ever become a proposal.
/// </summary>
internal static class RuleCommand
{
    internal const string Verb = "rule";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default) =>
        RunAsync(arguments, output, error, Console.In, !Console.IsInputRedirected, TimeProvider.System);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        TextReader input,
        bool isInteractive,
        TimeProvider clock)
    {
        if (arguments.Count == 0)
        {
            return await Usage(error, "The rule command requires a subcommand: list, show, propose, approve, or retire.").ConfigureAwait(false);
        }

        var subcommand = arguments[0];
        string? id = null;
        string? repository = null;
        string? scope = null;
        string? modality = null;
        string? type = null;
        string? value = null;
        string? quote = null;
        string? source = null;
        var asJson = false;

        for (var index = 1; index < arguments.Count; index++)
        {
            string? Next() => ++index < arguments.Count ? arguments[index] : null;
            switch (arguments[index])
            {
                case "--repository": repository = Next(); break;
                case "--scope": scope = Next(); break;
                case "--modality": modality = Next(); break;
                case "--type": type = Next(); break;
                case "--value": value = Next(); break;
                case "--quote": quote = Next(); break;
                case "--source": source = Next(); break;
                case "--json": asJson = true; break;
                default:
                    if (arguments[index].StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{arguments[index]}'.").ConfigureAwait(false);
                    }

                    if (id is not null)
                    {
                        return await Usage(error, "Only one rule id can be given.").ConfigureAwait(false);
                    }

                    id = arguments[index];
                    break;
            }
        }

        var root = Path.GetFullPath(repository ?? Directory.GetCurrentDirectory());
        var today = clock.GetLocalNow().ToString(RuleVocabulary.DateFormat, CultureInfo.InvariantCulture);

        try
        {
            switch (subcommand)
            {
                case "list":
                    return await ListAsync(root, asJson, output).ConfigureAwait(false);

                case "show":
                    return await ShowAsync(root, id, output, error).ConfigureAwait(false);

                case "propose":
                    return await ProposeAsync(root, id, scope, modality, type, value, quote, source, today, output, error).ConfigureAwait(false);

                case "approve":
                    return await ApproveAsync(root, id, today, output, error, input, isInteractive).ConfigureAwait(false);

                case "retire":
                    return await RetireAsync(root, id, today, output, error).ConfigureAwait(false);

                default:
                    return await Usage(error, $"Unknown rule subcommand '{subcommand}'.").ConfigureAwait(false);
            }
        }
        catch (RuleSchemaException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }
    }

    private static async Task<int> ListAsync(string root, bool asJson, TextWriter output)
    {
        var rules = RuleStore.LoadAll(root);
        if (asJson)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(rules, JsonOptions)).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (rules.Count == 0)
        {
            await output.WriteLineAsync($"No rules under {RuleVocabulary.RulesDirectory}.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        foreach (var rule in rules)
        {
            await output.WriteLineAsync(
                $"{rule.Id}  [{rule.State}]  {rule.Modality}  {rule.Check.Type}  {string.Join(',', rule.Scope)}  (reviewed {rule.Reviewed})")
                .ConfigureAwait(false);
        }

        return ExitCodes.Success;
    }

    private static async Task<int> ShowAsync(string root, string? id, TextWriter output, TextWriter error)
    {
        if (id is null)
        {
            return await Usage(error, "The show subcommand requires a rule id.").ConfigureAwait(false);
        }

        var rule = RuleStore.LoadAll(root).FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null)
        {
            await error.WriteLineAsync($"error: no rule '{id}'.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        await output.WriteAsync(RuleStore.Serialize(rule)).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> ProposeAsync(
        string root, string? id, string? scope, string? modality, string? type, string? value, string? quote, string? source,
        string today, TextWriter output, TextWriter error)
    {
        if (id is null || scope is null || modality is null || type is null || value is null || quote is null || source is null)
        {
            return await Usage(error, "propose requires <id> --scope --modality --type --value --quote --source.").ConfigureAwait(false);
        }

        if (File.Exists(RuleStore.PathFor(root, id)))
        {
            await error.WriteLineAsync($"error: rule '{id}' already exists.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        // Building the candidate through Parse applies the full closed schema, so a proposal can never be invalid.
        var candidate = new RuleDefinition(
            id,
            scope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            modality,
            new RuleCheck(type, value),
            new RuleQuote(quote, source),
            RuleVocabulary.Proposed,
            today);
        var validated = RuleStore.Parse(RuleStore.Serialize(candidate));
        RuleStore.Save(root, validated);
        await output.WriteLineAsync($"Proposed rule '{id}'. It is not enforced until a person runs: scout rule approve {id}").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> ApproveAsync(
        string root, string? id, string today, TextWriter output, TextWriter error, TextReader input, bool isInteractive)
    {
        if (id is null)
        {
            return await Usage(error, "The approve subcommand requires a rule id.").ConfigureAwait(false);
        }

        var rule = RuleStore.LoadAll(root).FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null)
        {
            await error.WriteLineAsync($"error: no rule '{id}'.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        if (!isInteractive)
        {
            await error.WriteLineAsync("error: approving a rule needs a person at a terminal; it cannot be done from a script or an agent.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var summary = string.Join(
            Environment.NewLine,
            "Rule:    " + rule.Id + " (" + rule.Modality + ")",
            "Scope:    " + string.Join(", ", rule.Scope),
            "Check:    " + rule.Check.Type + " " + rule.Check.Parameter,
            "Quote:    \"" + rule.Quote.Text + "\"",
            "Source:   " + rule.Quote.Source,
            "Type the rule id to make it enforceable: ");
        await output.WriteAsync(summary).ConfigureAwait(false);

        var answer = (await input.ReadLineAsync().ConfigureAwait(false))?.Trim();
        if (!string.Equals(answer, id, StringComparison.Ordinal))
        {
            await error.WriteLineAsync("Not approved: the id did not match.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        RuleStore.Save(root, rule with { State = RuleVocabulary.Active, Reviewed = today });
        await output.WriteLineAsync($"Approved '{id}'.").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> RetireAsync(string root, string? id, string today, TextWriter output, TextWriter error)
    {
        if (id is null)
        {
            return await Usage(error, "The retire subcommand requires a rule id.").ConfigureAwait(false);
        }

        var rule = RuleStore.LoadAll(root).FirstOrDefault(candidate => candidate.Id == id);
        if (rule is null)
        {
            await error.WriteLineAsync($"error: no rule '{id}'.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        RuleStore.Save(root, rule with { State = RuleVocabulary.Retired, Reviewed = today });
        await output.WriteLineAsync($"Retired '{id}'.").ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout rule list|show <id>|approve <id>|retire <id> [--repository <path>] [--json]\n" +
            "       scout rule propose <id> --scope <globs> --modality must|must_not|should " +
            "--type forbidden-call|naming|layering|linked-test --value <pattern-or-template> --quote \"<verbatim>\" --source <path#anchor>")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Scout.Cli.Rules;

/// <summary>Thrown when a rule file does not match the closed schema.</summary>
internal sealed class RuleSchemaException(string message) : Exception(message);

/// <summary>
/// Reads and writes rule files under <c>.scout/rules/</c>. The schema is closed: unknown properties, unknown
/// vocabulary, a missing verbatim quote, or an invalid pattern is an error, not something to be tolerated.
/// Files are written in a fixed property order so the same rule is always the same bytes.
/// </summary>
internal static class RuleStore
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    private static readonly string[] RuleKeys = ["id", "scope", "modality", "check", "quote", "state", "reviewed"];
    private static readonly string[] CheckKeys = ["type", "pattern", "forbiddenImport", "testPath"];
    private static readonly string[] QuoteKeys = ["text", "source"];

    internal static string DirectoryFor(string repositoryRoot) =>
        Path.Combine(repositoryRoot, RuleVocabulary.RulesDirectory.Replace('/', Path.DirectorySeparatorChar));

    internal static string PathFor(string repositoryRoot, string id) =>
        Path.Combine(DirectoryFor(repositoryRoot), id + RuleVocabulary.FileExtension);

    /// <summary>Loads every rule, in id order. A file that breaks the schema throws, naming the file.</summary>
    internal static IReadOnlyList<RuleDefinition> LoadAll(string repositoryRoot)
    {
        var directory = DirectoryFor(repositoryRoot);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var rules = new List<RuleDefinition>();
        foreach (var file in Directory.EnumerateFiles(directory, "*" + RuleVocabulary.FileExtension).Order(StringComparer.Ordinal))
        {
            try
            {
                var rule = Parse(File.ReadAllText(file));
                if (Path.GetFileNameWithoutExtension(file) != rule.Id)
                {
                    throw new RuleSchemaException($"the file name must be '{rule.Id}{RuleVocabulary.FileExtension}'.");
                }

                rules.Add(rule);
            }
            catch (Exception exception) when (exception is RuleSchemaException or JsonException)
            {
                throw new RuleSchemaException($"Rule file '{Path.GetFileName(file)}' is invalid: {exception.Message}");
            }
        }

        return rules.OrderBy(rule => rule.Id, StringComparer.Ordinal).ToArray();
    }

    internal static RuleDefinition Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new RuleSchemaException("expected a JSON object.");
        RejectUnknown(root, RuleKeys, "rule");

        var id = RequiredString(root, "id");
        if (!IdPattern.IsMatch(id))
        {
            throw new RuleSchemaException($"id '{id}' must be lowercase letters, digits, and hyphens.");
        }

        var scope = (root["scope"] as JsonArray ?? throw new RuleSchemaException("scope must be a non-empty array of globs."))
            .Select(node => node is JsonValue value && value.TryGetValue<string>(out var glob) && glob.Length > 0
                ? glob
                : throw new RuleSchemaException("scope entries must be non-empty strings."))
            .ToArray();
        if (scope.Length == 0)
        {
            throw new RuleSchemaException("scope must be a non-empty array of globs.");
        }

        var modality = OneOf(RequiredString(root, "modality"), RuleVocabulary.Modalities, "modality");
        var state = OneOf(RequiredString(root, "state"), RuleVocabulary.States, "state");

        var checkNode = root["check"] as JsonObject ?? throw new RuleSchemaException("check must be an object.");
        RejectUnknown(checkNode, CheckKeys, "check");
        var type = OneOf(RequiredString(checkNode, "type"), RuleVocabulary.CheckTypes, "check.type");
        var parameterName = RuleVocabulary.CheckParameter[type];
        foreach (var key in CheckKeys.Skip(1).Where(key => key != parameterName))
        {
            if (checkNode[key] is not null)
            {
                throw new RuleSchemaException($"check type '{type}' takes only '{parameterName}', not '{key}'.");
            }
        }

        var parameter = RequiredString(checkNode, parameterName);
        if (type != RuleVocabulary.LinkedTest)
        {
            try
            {
                _ = new Regex(parameter, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException exception)
            {
                throw new RuleSchemaException($"check.{parameterName} is not a valid regular expression: {exception.Message}");
            }
        }

        var quoteNode = root["quote"] as JsonObject ?? throw new RuleSchemaException("quote must be an object with the verbatim text and its source.");
        RejectUnknown(quoteNode, QuoteKeys, "quote");
        var quote = new RuleQuote(RequiredString(quoteNode, "text"), RequiredString(quoteNode, "source"));

        var reviewed = RequiredString(root, "reviewed");
        if (!DateTime.TryParseExact(reviewed, RuleVocabulary.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new RuleSchemaException($"reviewed must be a date written {RuleVocabulary.DateFormat}.");
        }

        return new RuleDefinition(id, scope, modality, new RuleCheck(type, parameter), quote, state, reviewed);
    }

    internal static string Serialize(RuleDefinition rule)
    {
        var check = new JsonObject { ["type"] = rule.Check.Type, [RuleVocabulary.CheckParameter[rule.Check.Type]] = rule.Check.Parameter };
        var node = new JsonObject
        {
            ["id"] = rule.Id,
            ["scope"] = new JsonArray(rule.Scope.Select(glob => (JsonNode)glob).ToArray()),
            ["modality"] = rule.Modality,
            ["check"] = check,
            ["quote"] = new JsonObject { ["text"] = rule.Quote.Text, ["source"] = rule.Quote.Source },
            ["state"] = rule.State,
            ["reviewed"] = rule.Reviewed
        };
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    internal static void Save(string repositoryRoot, RuleDefinition rule)
    {
        Directory.CreateDirectory(DirectoryFor(repositoryRoot));
        File.WriteAllText(PathFor(repositoryRoot, rule.Id), Serialize(rule));
    }

    private static string RequiredString(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new RuleSchemaException($"'{key}' is required and must be a non-empty string.");

    private static string OneOf(string value, string[] allowed, string name) =>
        allowed.Contains(value, StringComparer.Ordinal)
            ? value
            : throw new RuleSchemaException($"{name} '{value}' must be one of: {string.Join(", ", allowed)}.");

    private static void RejectUnknown(JsonObject node, string[] allowed, string name)
    {
        foreach (var key in node.Select(pair => pair.Key).Where(key => !allowed.Contains(key, StringComparer.Ordinal)))
        {
            throw new RuleSchemaException($"unknown property '{key}' in {name}.");
        }
    }
}

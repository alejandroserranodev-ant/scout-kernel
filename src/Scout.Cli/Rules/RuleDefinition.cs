namespace Scout.Cli.Rules;

/// <summary>The closed vocabularies a rule file may use. Anything else is rejected, never guessed.</summary>
internal static class RuleVocabulary
{
    internal const string RulesDirectory = ".scout/rules";
    internal const string FileExtension = ".json";
    internal const string DateFormat = "yyyy-MM-dd";

    internal const string Must = "must";
    internal const string MustNot = "must_not";
    internal const string Should = "should";

    internal const string Proposed = "proposed";
    internal const string Active = "active";
    internal const string Retired = "retired";

    internal const string ForbiddenCall = "forbidden-call";
    internal const string Naming = "naming";
    internal const string Layering = "layering";
    internal const string LinkedTest = "linked-test";

    internal static readonly string[] Modalities = [Must, MustNot, Should];
    internal static readonly string[] States = [Proposed, Active, Retired];
    internal static readonly string[] CheckTypes = [ForbiddenCall, Naming, Layering, LinkedTest];

    // The one parameter each check type needs. Kept here so the schema, the checker, and `rule propose` agree.
    internal static readonly IReadOnlyDictionary<string, string> CheckParameter = new Dictionary<string, string>
    {
        [ForbiddenCall] = "pattern",
        [Naming] = "pattern",
        [Layering] = "forbiddenImport",
        [LinkedTest] = "testPath"
    };
}

internal sealed record RuleQuote(string Text, string Source);

internal sealed record RuleCheck(string Type, string Parameter);

/// <summary>
/// A convention a repository wants enforced mechanically. It carries the verbatim text it came from, so a human
/// can see it is not Scout's invention, and a state, so nothing a document said can become binding on its own.
/// </summary>
internal sealed record RuleDefinition(
    string Id,
    IReadOnlyList<string> Scope,
    string Modality,
    RuleCheck Check,
    RuleQuote Quote,
    string State,
    string Reviewed);

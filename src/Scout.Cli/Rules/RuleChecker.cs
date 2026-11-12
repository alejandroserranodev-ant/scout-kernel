using System.Text;
using System.Text.RegularExpressions;

namespace Scout.Cli.Rules;

internal sealed record RuleViolation(string RuleId, string Modality, string Path, int? Line, string Message);

/// <summary>Matches repository-relative paths against simple globs: <c>*</c> within a directory, <c>**</c> across them.</summary>
internal static class Glob
{
    internal static bool IsMatch(string glob, string relativePath) =>
        ToRegex(glob).IsMatch(relativePath.Replace('\\', '/'));

    private static Regex ToRegex(string glob)
    {
        var pattern = new StringBuilder("^");
        for (var index = 0; index < glob.Length; index++)
        {
            var current = glob[index];
            if (current == '*' && index + 1 < glob.Length && glob[index + 1] == '*')
            {
                index++;
                // "**/" also matches no directory at all, so "src/**/*.ts" matches "src/a.ts".
                if (index + 1 < glob.Length && glob[index + 1] == '/')
                {
                    index++;
                    pattern.Append("(?:.*/)?");
                }
                else
                {
                    pattern.Append(".*");
                }
            }
            else if (current == '*')
            {
                pattern.Append("[^/]*");
            }
            else if (current == '?')
            {
                pattern.Append("[^/]");
            }
            else
            {
                pattern.Append(Regex.Escape(current.ToString()));
            }
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.CultureInvariant);
    }
}

/// <summary>
/// Runs active rules against files. Every check is a textual or path test, so what it reports is exactly what a
/// reader can verify by opening the file: it never claims a semantic understanding it does not have.
/// </summary>
internal static class RuleChecker
{
    // Lines a layering check looks at: the ones that name a dependency in the languages Scout analyzes.
    private static readonly Regex DependencyLine = new(@"^\s*(?:import\b|export\b.*\bfrom\b|using\b|.*\brequire\s*\()", RegexOptions.CultureInvariant);

    internal static bool AppliesTo(RuleDefinition rule, string relativePath) =>
        rule.Scope.Any(glob => Glob.IsMatch(glob, relativePath));

    internal static IReadOnlyList<RuleViolation> Check(
        string repositoryRoot,
        IEnumerable<RuleDefinition> rules,
        IEnumerable<string> relativePaths)
    {
        var activeRules = rules.Where(rule => rule.State == RuleVocabulary.Active).ToArray();
        var violations = new List<RuleViolation>();

        foreach (var path in relativePaths.Order(StringComparer.Ordinal))
        {
            var applicable = activeRules.Where(rule => AppliesTo(rule, path)).ToArray();
            if (applicable.Length == 0)
            {
                continue;
            }

            var absolute = Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));
            var lines = File.Exists(absolute) ? File.ReadAllLines(absolute) : [];

            foreach (var rule in applicable)
            {
                violations.AddRange(CheckOne(repositoryRoot, rule, path, lines));
            }
        }

        return violations;
    }

    private static IEnumerable<RuleViolation> CheckOne(string repositoryRoot, RuleDefinition rule, string path, string[] lines)
    {
        var parameter = rule.Check.Parameter;
        switch (rule.Check.Type)
        {
            case RuleVocabulary.ForbiddenCall:
                var call = new Regex(parameter, RegexOptions.CultureInvariant);
                for (var index = 0; index < lines.Length; index++)
                {
                    if (call.IsMatch(lines[index]))
                    {
                        yield return new RuleViolation(rule.Id, rule.Modality, path, index + 1, $"matches forbidden pattern /{parameter}/.");
                    }
                }

                break;

            case RuleVocabulary.Layering:
                var forbidden = new Regex(parameter, RegexOptions.CultureInvariant);
                for (var index = 0; index < lines.Length; index++)
                {
                    if (DependencyLine.IsMatch(lines[index]) && forbidden.IsMatch(lines[index]))
                    {
                        yield return new RuleViolation(rule.Id, rule.Modality, path, index + 1, $"depends on something matching /{parameter}/.");
                    }
                }

                break;

            case RuleVocabulary.Naming:
                var fileName = Path.GetFileName(path);
                if (!new Regex(parameter, RegexOptions.CultureInvariant).IsMatch(fileName))
                {
                    yield return new RuleViolation(rule.Id, rule.Modality, path, null, $"file name '{fileName}' does not match /{parameter}/.");
                }

                break;

            case RuleVocabulary.LinkedTest:
                var expected = ExpandTestPath(parameter, path);
                if (!File.Exists(Path.Combine(repositoryRoot, expected.Replace('/', Path.DirectorySeparatorChar))))
                {
                    yield return new RuleViolation(rule.Id, rule.Modality, path, null, $"expected a linked test at '{expected}'.");
                }

                break;
        }
    }

    // {dir} is the file's directory, {name} its name without extension, {ext} its extension with the dot.
    internal static string ExpandTestPath(string template, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var directory = slash < 0 ? string.Empty : normalized[..slash];
        return template
            .Replace("{dir}", directory, StringComparison.Ordinal)
            .Replace("{name}", Path.GetFileNameWithoutExtension(normalized), StringComparison.Ordinal)
            .Replace("{ext}", Path.GetExtension(normalized), StringComparison.Ordinal)
            .TrimStart('/');
    }
}

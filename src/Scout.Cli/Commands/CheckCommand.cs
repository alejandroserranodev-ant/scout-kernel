using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Scout.Cli.Rules;

namespace Scout.Cli.Commands;

/// <summary>
/// <c>scout check [--staged] [--json]</c>: runs the repository's active rules over its files (or over only the
/// staged ones) and reports each violation with the file, line, and the rule's own quoted text. Exit code
/// <see cref="ExitCodes.Violations"/> when a <c>must</c>/<c>must_not</c> rule is broken; <c>should</c> rules only warn.
/// </summary>
internal static class CheckCommand
{
    internal const string Verb = "check";
    internal const int DefaultLimit = 50;

    // Used only when git cannot list files: directories that hold dependencies or build output, never source.
    private static readonly string[] SkippedDirectories = [".git", "node_modules", "bin", "obj"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? repository = null;
        var staged = false;
        var asJson = false;
        var limit = DefaultLimit;

        for (var index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--staged": staged = true; break;
                case "--json": asJson = true; break;
                case "--repository":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --repository option requires a path.").ConfigureAwait(false);
                    }

                    repository = arguments[index];
                    break;
                case "--limit":
                    if (++index >= arguments.Count || !int.TryParse(arguments[index], out limit) || limit <= 0)
                    {
                        return await Usage(error, "The --limit option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;
                default:
                    return await Usage(error, $"Unknown argument '{arguments[index]}'.").ConfigureAwait(false);
            }
        }

        var root = Path.GetFullPath(repository ?? Directory.GetCurrentDirectory());
        IReadOnlyList<RuleDefinition> rules;
        try
        {
            rules = RuleStore.LoadAll(root);
        }
        catch (RuleSchemaException exception)
        {
            await error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var active = rules.Count(rule => rule.State == RuleVocabulary.Active);
        if (active == 0)
        {
            await output.WriteLineAsync($"No active rules under {RuleVocabulary.RulesDirectory}; nothing to check.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var files = await ListFilesAsync(root, staged, cancellationToken).ConfigureAwait(false);
        var violations = RuleChecker.Check(root, rules, files);
        var quotes = rules.ToDictionary(rule => rule.Id, rule => rule.Quote.Text, StringComparer.Ordinal);
        var blocking = violations.Count(violation => violation.Modality != RuleVocabulary.Should);
        var shown = violations.Take(limit).ToArray();

        if (asJson)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                new { ActiveRules = active, FilesChecked = files.Count, Total = violations.Count, Blocking = blocking, Violations = shown },
                JsonOptions)).ConfigureAwait(false);
        }
        else
        {
            var builder = new StringBuilder()
                .Append(active).Append(" active rule(s), ").Append(files.Count).Append(" file(s) checked, ")
                .Append(violations.Count).AppendLine(" violation(s).");
            foreach (var violation in shown)
            {
                builder.Append('[').Append(violation.Modality).Append("] ").Append(violation.Path);
                if (violation.Line is { } line)
                {
                    builder.Append(':').Append(line);
                }

                builder.Append("  ").Append(violation.RuleId).Append(": ").Append(violation.Message)
                    .Append("  Rule says: \"").Append(quotes[violation.RuleId]).AppendLine("\"");
            }

            if (violations.Count > shown.Length)
            {
                builder.Append("... ").Append(violations.Count - shown.Length).AppendLine(" more (raise --limit).");
            }

            await output.WriteLineAsync(builder.ToString().TrimEnd()).ConfigureAwait(false);
        }

        return blocking > 0 ? ExitCodes.Violations : ExitCodes.Success;
    }

    private static async Task<IReadOnlyList<string>> ListFilesAsync(string root, bool staged, CancellationToken cancellationToken)
    {
        var arguments = staged
            ? new[] { "diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z" }
            : ["ls-files", "--cached", "--others", "--exclude-standard", "-z"];
        var listed = await RunGitAsync(root, arguments, cancellationToken).ConfigureAwait(false);
        if (listed is not null)
        {
            return listed;
        }

        // Not a git repository: --staged has no meaning, and every file under the root is a candidate.
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => SkippedDirectories.Contains(segment, StringComparer.Ordinal)))
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>?> RunGitAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(root);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            _ = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? (await standardOutput.ConfigureAwait(false)).Split('\0', StringSplitOptions.RemoveEmptyEntries)
                : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync("usage: scout check [--repository <path>] [--staged] [--limit <n>] [--json]").ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

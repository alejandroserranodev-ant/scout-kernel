using System.Globalization;
using System.Text;
using System.Text.Json;
using Scout.Analysis.Sql;
using Scout.Cli.Db;
using Scout.Core.Repository;

namespace Scout.Cli.Commands;

/// <summary>
/// <c>scout db check</c>: reviews a repository's SQL schema declarations and, when a database connection is
/// available, compares them with the live database. Without a connection it still reports the static findings.
/// </summary>
/// <remarks>
/// The connection string is read from an environment variable the caller names, handed to <c>psql</c>, and never
/// printed or stored. The live session is read-only and reads catalog views, never table rows.
/// </remarks>
internal static class DbCommand
{
    internal const string Verb = "db";
    internal const string DefaultConnectionEnvironmentVariable = "SCOUT_DB_URL";
    internal const int DefaultLimit = 50;

    private const string CheckSubcommand = "check";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default) =>
        RunAsync(arguments, output, error, Environment.GetEnvironmentVariable, sourceFactory: null, cancellationToken);

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        Func<string, string?> readEnvironment,
        Func<string, string, IDatabaseSnapshotSource>? sourceFactory,
        CancellationToken cancellationToken)
    {
        if (arguments.Count == 0 || arguments[0] != CheckSubcommand)
        {
            return await Usage(error, "The db command currently supports only 'check': scout db check <repository>.").ConfigureAwait(false);
        }

        string? repository = null;
        var connectionVariable = DefaultConnectionEnvironmentVariable;
        var psqlCommand = PsqlSnapshotSource.DefaultPsqlCommand;
        var limit = DefaultLimit;
        var asJson = false;

        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--connection-env":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --connection-env option requires a variable name.").ConfigureAwait(false);
                    }

                    connectionVariable = arguments[index];
                    break;

                case "--psql":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --psql option requires a command or path.").ConfigureAwait(false);
                    }

                    psqlCommand = arguments[index];
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

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (repository is not null)
                    {
                        return await Usage(error, "Only one repository can be checked at a time.").ConfigureAwait(false);
                    }

                    repository = argument;
                    break;
            }
        }

        if (repository is null)
        {
            return await Usage(error, "The db check command requires a repository path.").ConfigureAwait(false);
        }

        var descriptor = await new RepositoryDiscovery([".sql"]).DiscoverAsync(repository, cancellationToken).ConfigureAwait(false);
        var models = new List<SqlSchemaModel>();
        foreach (var file in descriptor.Files.Where(file => file.RelativePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)))
        {
            var text = await File.ReadAllTextAsync(RepositoryPath.Resolve(descriptor.RootPath, file.RelativePath), cancellationToken).ConfigureAwait(false);
            models.Add(SqlSchemaReader.Read(text));
        }

        if (models.Sum(model => model.Tables.Count) == 0)
        {
            await output.WriteLineAsync("No SQL table declarations found; nothing to check.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        string liveStatus;
        DatabaseSnapshot? snapshot = null;
        var connection = readEnvironment(connectionVariable);
        if (string.IsNullOrWhiteSpace(connection))
        {
            liveStatus = $"not checked: environment variable {connectionVariable} is not set";
        }
        else
        {
            try
            {
                var source = sourceFactory is null
                    ? new PsqlSnapshotSource(psqlCommand, connection)
                    : sourceFactory(psqlCommand, connection);
                snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
                liveStatus = "checked";
            }
            catch (InvalidOperationException exception)
            {
                liveStatus = $"not checked: {exception.Message}";
            }
            catch (JsonException exception)
            {
                liveStatus = $"not checked: unreadable snapshot ({exception.Message})";
            }
        }

        var findings = SchemaChecker.Check(models, snapshot)
            .OrderBy(finding => finding.Severity switch { FindingSeverities.High => 0, FindingSeverities.Medium => 1, _ => 2 })
            .ToArray();
        var shown = findings.Take(limit).ToArray();

        if (asJson)
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(
                new { Live = liveStatus, Total = findings.Length, Shown = shown.Length, Findings = shown },
                JsonOptions)).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var builder = new StringBuilder();
        builder.Append("live database: ").AppendLine(liveStatus);
        builder.Append(findings.Length).AppendLine(" finding(s).");
        foreach (var finding in shown)
        {
            builder.Append('[').Append(finding.Severity).Append("] ").Append(finding.Code).Append(' ').AppendLine(finding.Message);
        }

        if (findings.Length > shown.Length)
        {
            builder.Append("... ").Append(findings.Length - shown.Length).AppendLine(" more (raise --limit).");
        }

        await output.WriteLineAsync(builder.ToString().TrimEnd()).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout db check <repository> [--connection-env <VAR>] [--psql <command>] [--limit <n>] [--json]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

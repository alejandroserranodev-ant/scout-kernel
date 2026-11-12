using System.ComponentModel;
using System.Diagnostics;

namespace Scout.Cli.Db;

/// <summary>
/// Reads the schema snapshot by running the PostgreSQL <c>psql</c> client, so Scout needs no database driver
/// dependency. The session is set read-only first and the single query reads catalog views only.
/// </summary>
internal sealed class PsqlSnapshotSource(string psqlCommand, string connection) : IDatabaseSnapshotSource
{
    internal const string DefaultPsqlCommand = "psql";

    private const string ReadOnlySession = "SET default_transaction_read_only = on";

    private const string SnapshotQuery = """
        select json_build_object(
          'tables', (select coalesce(json_agg(json_build_object('schema', n.nspname, 'name', c.relname, 'rls', c.relrowsecurity)), '[]'::json)
                     from pg_class c join pg_namespace n on n.oid = c.relnamespace
                     where c.relkind in ('r', 'p') and n.nspname not in ('pg_catalog', 'information_schema') and n.nspname !~ '^pg_'),
          'policies', (select coalesce(json_agg(json_build_object('schema', schemaname, 'table', tablename, 'name', policyname)), '[]'::json)
                       from pg_policies),
          'columns', (select coalesce(json_agg(json_build_object('schema', table_schema, 'table', table_name, 'name', column_name)), '[]'::json)
                      from information_schema.columns where table_schema not in ('pg_catalog', 'information_schema'))
        )
        """;

    public async Task<DatabaseSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(psqlCommand)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1", "-c", ReadOnlySession, "-c", SnapshotQuery, "-d", connection })
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{psqlCommand}'.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException($"Could not run '{psqlCommand}': {exception.Message}", exception);
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errors = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                // psql's message can echo parts of the connection; report only its first line.
                var first = (await errors.ConfigureAwait(false)).Split('\n', 2)[0].Trim();
                throw new InvalidOperationException($"psql failed (exit {process.ExitCode}): {first}");
            }

            return DatabaseSnapshot.FromJson(await output.ConfigureAwait(false));
        }
    }
}

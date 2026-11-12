using Scout.Analysis.Sql;

namespace Scout.Cli.Db;

internal static class FindingSeverities
{
    internal const string High = "high";
    internal const string Medium = "medium";
    internal const string Info = "info";
}

internal static class FindingCodes
{
    internal const string TableWithoutRowSecurity = "TABLE_WITHOUT_RLS";
    internal const string RowSecurityWithoutPolicy = "RLS_WITHOUT_POLICY";
    internal const string TableMissingInDatabase = "TABLE_MISSING_IN_DATABASE";
    internal const string TableNotDeclared = "TABLE_NOT_DECLARED";
    internal const string RowSecurityDisabledInDatabase = "RLS_DISABLED_IN_DATABASE";
    internal const string PolicyMissingInDatabase = "POLICY_MISSING_IN_DATABASE";
    internal const string PolicyNotDeclared = "POLICY_NOT_DECLARED";
    internal const string ColumnMissingInDatabase = "COLUMN_MISSING_IN_DATABASE";
    internal const string ColumnNotDeclared = "COLUMN_NOT_DECLARED";
}

internal sealed record SchemaFinding(string Severity, string Code, string Subject, string Message);

/// <summary>
/// Compares what SQL files declare against (optionally) what a live database reports. The static findings need
/// no connection; the live ones are added only when a snapshot is supplied.
/// </summary>
internal static class SchemaChecker
{
    // Schemas the SQL files never declare a table in are not Scout's business: a managed platform's own schemas
    // would otherwise drown every real finding.
    internal static IReadOnlyList<SchemaFinding> Check(IReadOnlyList<SqlSchemaModel> declaredInOrder, DatabaseSnapshot? live)
    {
        var columns = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var rowSecurity = new Dictionary<string, bool>(StringComparer.Ordinal);
        var policies = new HashSet<(string Table, string Name)>();

        foreach (var model in declaredInOrder)
        {
            foreach (var table in model.Tables)
            {
                if (!columns.TryGetValue(table.QualifiedName, out var set))
                {
                    columns[table.QualifiedName] = set = new HashSet<string>(StringComparer.Ordinal);
                }

                set.UnionWith(table.Columns.Select(column => column.Name));
            }

            foreach (var added in model.AddedColumns)
            {
                if (columns.TryGetValue($"{added.Schema}.{added.Table}", out var set))
                {
                    set.Add(added.Column.Name);
                }
            }

            foreach (var switchSeen in model.RowSecurity)
            {
                rowSecurity[switchSeen.QualifiedTable] = switchSeen.Enabled;
            }

            foreach (var policy in model.Policies)
            {
                policies.Add((policy.QualifiedTable, policy.Name));
            }
        }

        var findings = new List<SchemaFinding>();

        foreach (var table in columns.Keys.Order(StringComparer.Ordinal))
        {
            if (!rowSecurity.GetValueOrDefault(table))
            {
                findings.Add(new SchemaFinding(
                    FindingSeverities.High,
                    FindingCodes.TableWithoutRowSecurity,
                    table,
                    $"Table '{table}' never has row level security enabled in the SQL files."));
            }
            else if (!policies.Any(policy => policy.Table == table))
            {
                findings.Add(new SchemaFinding(
                    FindingSeverities.Info,
                    FindingCodes.RowSecurityWithoutPolicy,
                    table,
                    $"Table '{table}' enables row level security but declares no policy, so only privileged roles can read it."));
            }
        }

        if (live is not null)
        {
            AddLiveFindings(findings, columns, rowSecurity, policies, live);
        }

        return findings;
    }

    private static void AddLiveFindings(
        List<SchemaFinding> findings,
        Dictionary<string, HashSet<string>> declaredColumns,
        Dictionary<string, bool> declaredRowSecurity,
        HashSet<(string Table, string Name)> declaredPolicies,
        DatabaseSnapshot live)
    {
        var declaredSchemas = declaredColumns.Keys.Select(name => name[..name.IndexOf('.')]).ToHashSet(StringComparer.Ordinal);
        var liveTables = live.Tables
            .Where(table => declaredSchemas.Contains(table.Schema))
            .ToDictionary(table => $"{table.Schema}.{table.Name}", StringComparer.Ordinal);

        foreach (var table in declaredColumns.Keys.Order(StringComparer.Ordinal))
        {
            if (!liveTables.TryGetValue(table, out var liveTable))
            {
                findings.Add(new SchemaFinding(
                    FindingSeverities.Medium,
                    FindingCodes.TableMissingInDatabase,
                    table,
                    $"Table '{table}' is declared in the SQL files but does not exist in the database."));
                continue;
            }

            if (!liveTable.RowSecurity)
            {
                var declaredEnabled = declaredRowSecurity.GetValueOrDefault(table);
                findings.Add(new SchemaFinding(
                    FindingSeverities.High,
                    FindingCodes.RowSecurityDisabledInDatabase,
                    table,
                    declaredEnabled
                        ? $"Table '{table}' enables row level security in the SQL files but it is OFF in the database."
                        : $"Table '{table}' has row level security OFF in the database."));
            }

            var liveColumns = live.Columns
                .Where(column => $"{column.Schema}.{column.Table}" == table)
                .Select(column => column.Name)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var column in declaredColumns[table].Where(name => !liveColumns.Contains(name)).Order(StringComparer.Ordinal))
            {
                findings.Add(new SchemaFinding(
                    FindingSeverities.Medium,
                    FindingCodes.ColumnMissingInDatabase,
                    $"{table}.{column}",
                    $"Column '{table}.{column}' is declared in the SQL files but absent from the database."));
            }

            foreach (var column in liveColumns.Where(name => !declaredColumns[table].Contains(name)).Order(StringComparer.Ordinal))
            {
                findings.Add(new SchemaFinding(
                    FindingSeverities.Info,
                    FindingCodes.ColumnNotDeclared,
                    $"{table}.{column}",
                    $"Column '{table}.{column}' exists in the database but no SQL file declares it (or a file drops it)."));
            }
        }

        foreach (var table in liveTables.Keys.Where(name => !declaredColumns.ContainsKey(name)).Order(StringComparer.Ordinal))
        {
            findings.Add(new SchemaFinding(
                FindingSeverities.Medium,
                FindingCodes.TableNotDeclared,
                table,
                $"Table '{table}' exists in the database but no SQL file creates it."));
        }

        var livePolicies = live.Policies
            .Where(policy => declaredSchemas.Contains(policy.Schema))
            .Select(policy => ($"{policy.Schema}.{policy.Table}", policy.Name))
            .ToHashSet();
        foreach (var (table, name) in declaredPolicies.Where(policy => !livePolicies.Contains(policy)).OrderBy(p => p.Table, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            findings.Add(new SchemaFinding(
                FindingSeverities.High,
                FindingCodes.PolicyMissingInDatabase,
                $"{table}:{name}",
                $"Policy '{name}' on '{table}' is declared in the SQL files but is not active in the database."));
        }

        foreach (var (table, name) in livePolicies.Where(policy => !declaredPolicies.Contains(policy)).OrderBy(p => p.Item1, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            findings.Add(new SchemaFinding(
                FindingSeverities.Info,
                FindingCodes.PolicyNotDeclared,
                $"{table}:{name}",
                $"Policy '{name}' on '{table}' is active in the database but no SQL file declares it."));
        }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace Scout.Analysis.Sql;

/// <summary>A column declared by a table definition or an <c>ADD COLUMN</c>.</summary>
public sealed record SqlColumn(string Name, string Type, int Offset, int Length);

/// <summary>A table declared by <c>CREATE TABLE</c>, with the columns that statement lists.</summary>
public sealed record SqlTable(string Schema, string Name, int Offset, int Length, IReadOnlyList<SqlColumn> Columns)
{
    /// <summary>Gets the schema-qualified name.</summary>
    public string QualifiedName => $"{Schema}.{Name}";
}

/// <summary>A column added to a table by <c>ALTER TABLE ... ADD COLUMN</c>.</summary>
public sealed record SqlAddedColumn(string Schema, string Table, SqlColumn Column);

/// <summary>A row-level-security switch seen on a table.</summary>
public sealed record SqlRowSecurity(string Schema, string Table, bool Enabled, int Offset, int Length)
{
    /// <summary>Gets the schema-qualified table name.</summary>
    public string QualifiedTable => $"{Schema}.{Table}";
}

/// <summary>A row-level-security policy declared by <c>CREATE POLICY</c>.</summary>
public sealed record SqlPolicy(
    string Name,
    string Schema,
    string Table,
    string Command,
    IReadOnlyList<string> Roles,
    int Offset,
    int Length)
{
    /// <summary>Gets the schema-qualified table name.</summary>
    public string QualifiedTable => $"{Schema}.{Table}";
}

/// <summary>A function, procedure, view, or index declaration: a named thing without columns.</summary>
public sealed record SqlNamedObject(string Kind, string Schema, string Name, string? Table, int Offset, int Length)
{
    /// <summary>Gets the schema-qualified name.</summary>
    public string QualifiedName => $"{Schema}.{Name}";
}

/// <summary>Everything a set of SQL statements declares, as observed, with no database consulted.</summary>
public sealed record SqlSchemaModel(
    IReadOnlyList<SqlTable> Tables,
    IReadOnlyList<SqlAddedColumn> AddedColumns,
    IReadOnlyList<SqlRowSecurity> RowSecurity,
    IReadOnlyList<SqlPolicy> Policies,
    IReadOnlyList<SqlNamedObject> NamedObjects);

/// <summary>
/// Reads schema declarations out of SQL text. Deliberately a statement-level reader, not a SQL parser: it
/// recognizes the declaration shapes a schema is made of and ignores everything else (data changes, grants,
/// function bodies). Dollar-quoted bodies, string literals, and comments are never mistaken for statements.
/// </summary>
public static class SqlSchemaReader
{
    /// <summary>The schema assumed for an unqualified name, as in PostgreSQL's default search path.</summary>
    public const string DefaultSchema = "public";

    /// <summary>The policy command recorded when <c>FOR</c> is omitted.</summary>
    public const string DefaultPolicyCommand = "all";

    private const string Ident = "(?:\"[^\"]+\"|[A-Za-z_][\\w$]*)";
    private const string Qualified = $"{Ident}(?:\\s*\\.\\s*{Ident})?";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    private static readonly Regex CreateTable = new(
        $@"^create\s+(?:or\s+replace\s+)?(?:(?:global\s+|local\s+)?(?:temporary\s+|temp\s+)|unlogged\s+)?table\s+(?:if\s+not\s+exists\s+)?(?<name>{Qualified})\s*\(",
        Options);

    private static readonly Regex AlterTable = new(
        $@"^alter\s+table\s+(?:if\s+exists\s+)?(?:only\s+)?(?<name>{Qualified})\s+(?<rest>.*)$",
        Options);

    private static readonly Regex AddColumnClause = new(
        $@"^add\s+column\s+(?:if\s+not\s+exists\s+)?(?<col>{Ident})\s+(?<type>.*)$",
        Options);

    private static readonly Regex CreatePolicy = new(
        $@"^create\s+policy\s+(?<policy>{Ident})\s+on\s+(?<table>{Qualified})(?<rest>.*)$",
        Options);

    private static readonly Regex PolicyCommand = new(@"\bfor\s+(?<cmd>all|select|insert|update|delete)\b", Options);

    private static readonly Regex PolicyRoles = new(@"\bto\s+(?<roles>.+?)(?=\s+using\b|\s+with\s+check\b|$)", Options);

    private static readonly Regex CreateRoutine = new(
        $@"^create\s+(?:or\s+replace\s+)?(?<kind>function|procedure)\s+(?<name>{Qualified})",
        Options);

    private static readonly Regex CreateView = new(
        $@"^create\s+(?:or\s+replace\s+)?(?:(?:temporary\s+|temp\s+|recursive\s+)?(?:materialized\s+)?)view\s+(?:if\s+not\s+exists\s+)?(?<name>{Qualified})",
        Options);

    private static readonly Regex CreateIndex = new(
        $@"^create\s+(?:unique\s+)?index\s+(?:concurrently\s+)?(?:if\s+not\s+exists\s+)?(?<name>{Ident})\s+on\s+(?:only\s+)?(?<table>{Qualified})",
        Options);

    private static readonly string[] ConstraintLeaders =
        ["constraint", "primary", "foreign", "unique", "check", "exclude", "like", "period"];

    private static readonly Regex TypeEnd = new(
        @"\s+(?:not\s+null|null|default|primary\s+key|references|check|unique|generated|constraint|collate)\b",
        Options);

    /// <summary>Reads the declarations in <paramref name="sql"/>; offsets index into that same text.</summary>
    public static SqlSchemaModel Read(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);

        var tables = new List<SqlTable>();
        var added = new List<SqlAddedColumn>();
        var security = new List<SqlRowSecurity>();
        var policies = new List<SqlPolicy>();
        var named = new List<SqlNamedObject>();

        foreach (var (offset, text) in SplitStatements(sql))
        {
            var length = text.Length;

            if (CreateTable.Match(text) is { Success: true } table)
            {
                var (schema, name) = SplitName(table.Groups["name"].Value);
                var bodyStart = table.Index + table.Length;
                tables.Add(new SqlTable(schema, name, offset, length, ReadColumns(text, bodyStart, offset)));
            }
            else if (AlterTable.Match(text) is { Success: true } alter)
            {
                var (schema, name) = SplitName(alter.Groups["name"].Value);
                var rest = alter.Groups["rest"].Value.Trim();
                var restOffset = offset + alter.Groups["rest"].Index;
                if (Regex.IsMatch(rest, @"^enable\s+row\s+level\s+security\b", Options))
                {
                    security.Add(new SqlRowSecurity(schema, name, Enabled: true, offset, length));
                }
                else if (Regex.IsMatch(rest, @"^disable\s+row\s+level\s+security\b", Options))
                {
                    security.Add(new SqlRowSecurity(schema, name, Enabled: false, offset, length));
                }
                else if (AddColumnClause.Match(rest) is { Success: true } add)
                {
                    var column = new SqlColumn(
                        Unquote(add.Groups["col"].Value),
                        CleanType(add.Groups["type"].Value),
                        restOffset,
                        add.Length);
                    added.Add(new SqlAddedColumn(schema, name, column));
                }
            }
            else if (CreatePolicy.Match(text) is { Success: true } policy)
            {
                var (schema, tableName) = SplitName(policy.Groups["table"].Value);
                var rest = policy.Groups["rest"].Value;
                var command = PolicyCommand.Match(rest) is { Success: true } c
                    ? c.Groups["cmd"].Value.ToLowerInvariant()
                    : DefaultPolicyCommand;
                var roles = PolicyRoles.Match(rest) is { Success: true } r
                    ? r.Groups["roles"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .Select(Unquote).ToArray()
                    : [];
                policies.Add(new SqlPolicy(Unquote(policy.Groups["policy"].Value), schema, tableName, command, roles, offset, length));
            }
            else if (CreateRoutine.Match(text) is { Success: true } routine)
            {
                var (schema, name) = SplitName(routine.Groups["name"].Value);
                named.Add(new SqlNamedObject(routine.Groups["kind"].Value.ToLowerInvariant(), schema, name, null, offset, length));
            }
            else if (CreateView.Match(text) is { Success: true } view)
            {
                var (schema, name) = SplitName(view.Groups["name"].Value);
                named.Add(new SqlNamedObject("view", schema, name, null, offset, length));
            }
            else if (CreateIndex.Match(text) is { Success: true } index)
            {
                var (schema, tableName) = SplitName(index.Groups["table"].Value);
                named.Add(new SqlNamedObject("index", schema, Unquote(index.Groups["name"].Value), $"{schema}.{tableName}", offset, length));
            }
        }

        return new SqlSchemaModel(tables, added, security, policies, named);
    }

    private static List<SqlColumn> ReadColumns(string statement, int bodyStart, int statementOffset)
    {
        var columns = new List<SqlColumn>();
        var depth = 1;
        var itemStart = bodyStart;
        var quote = '\0';

        for (var index = bodyStart; index < statement.Length && depth > 0; index++)
        {
            var current = statement[index];
            if (quote != '\0')
            {
                if (current == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (current)
            {
                case '\'' or '"':
                    quote = current;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                    {
                        AddColumn(statement, itemStart, index, statementOffset, columns);
                    }

                    break;
                case ',' when depth == 1:
                    AddColumn(statement, itemStart, index, statementOffset, columns);
                    itemStart = index + 1;
                    break;
            }
        }

        return columns;
    }

    private static void AddColumn(string statement, int start, int end, int statementOffset, List<SqlColumn> columns)
    {
        var item = statement[start..end];
        var trimmed = item.TrimStart();
        if (trimmed.Length == 0)
        {
            return;
        }

        var leading = item.Length - trimmed.Length;
        var match = Regex.Match(trimmed, $"^(?<col>{Ident})\\s*(?<type>.*)$", Options);
        if (!match.Success)
        {
            return;
        }

        var name = match.Groups["col"].Value;
        if (ConstraintLeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        columns.Add(new SqlColumn(Unquote(name), CleanType(match.Groups["type"].Value), statementOffset + start + leading, trimmed.TrimEnd().Length));
    }

    private static string CleanType(string raw)
    {
        var end = TypeEnd.Match(" " + raw.Trim());
        var type = end.Success ? raw.Trim()[..Math.Max(0, end.Index - 1)] : raw.Trim();
        return Regex.Replace(type, @"\s+", " ").Trim();
    }

    private static (string Schema, string Name) SplitName(string qualified)
    {
        var parts = Regex.Matches(qualified, Ident, Options).Select(m => Unquote(m.Value)).ToArray();
        return parts.Length >= 2 ? (parts[^2], parts[^1]) : (DefaultSchema, parts[0]);
    }

    private static string Unquote(string identifier) =>
        identifier.Length >= 2 && identifier[0] == '"' ? identifier[1..^1] : identifier.ToLowerInvariant();

    // Statements end at a top-level semicolon. Comments become spaces of equal length (newlines kept) so every
    // offset into the masked text is also valid in the original; string literals and dollar-quoted bodies are
    // kept but skipped over, so a semicolon inside them does not end a statement.
    private static IEnumerable<(int Offset, string Text)> SplitStatements(string sql)
    {
        var masked = new StringBuilder(sql.Length);
        var statementStart = 0;
        var index = 0;

        while (index < sql.Length)
        {
            var current = sql[index];
            var next = index + 1 < sql.Length ? sql[index + 1] : '\0';

            if (current == '-' && next == '-')
            {
                while (index < sql.Length && sql[index] != '\n')
                {
                    masked.Append(' ');
                    index++;
                }

                continue;
            }

            if (current == '/' && next == '*')
            {
                var close = sql.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var stop = close < 0 ? sql.Length : close + 2;
                for (; index < stop; index++)
                {
                    masked.Append(sql[index] == '\n' ? '\n' : ' ');
                }

                continue;
            }

            if (current is '\'' or '"')
            {
                var close = index + 1;
                while (close < sql.Length)
                {
                    if (sql[close] == current)
                    {
                        if (close + 1 < sql.Length && sql[close + 1] == current)
                        {
                            close += 2;
                            continue;
                        }

                        break;
                    }

                    close++;
                }

                var stop = Math.Min(close + 1, sql.Length);
                masked.Append(sql, index, stop - index);
                index = stop;
                continue;
            }

            if (current == '$')
            {
                var tagEnd = index + 1;
                while (tagEnd < sql.Length && (char.IsLetterOrDigit(sql[tagEnd]) || sql[tagEnd] == '_'))
                {
                    tagEnd++;
                }

                if (tagEnd < sql.Length && sql[tagEnd] == '$')
                {
                    var tag = sql[index..(tagEnd + 1)];
                    var close = sql.IndexOf(tag, tagEnd + 1, StringComparison.Ordinal);
                    var stop = close < 0 ? sql.Length : close + tag.Length;
                    masked.Append(sql, index, stop - index);
                    index = stop;
                    continue;
                }
            }

            if (current == ';')
            {
                foreach (var statement in Emit(masked.ToString(), statementStart))
                {
                    yield return statement;
                }

                index++;
                statementStart = index;
                masked.Clear();
                continue;
            }

            masked.Append(current);
            index++;
        }

        foreach (var statement in Emit(masked.ToString(), statementStart))
        {
            yield return statement;
        }
    }

    private static IEnumerable<(int Offset, string Text)> Emit(string text, int start)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length > 0)
        {
            yield return (start + (text.Length - trimmed.Length), trimmed.TrimEnd());
        }
    }
}

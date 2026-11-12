using System.Collections.ObjectModel;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.Sql;

/// <summary>
/// Extracts schema declarations (tables, columns, row-level-security switches and policies, functions, views,
/// indexes) from SQL files into language-independent structural facts.
/// </summary>
/// <remarks>
/// Only what the files declare is recorded. Whether a database actually matches them is a separate question that
/// needs a live connection; see <c>scout db check</c>. Statement bodies, data changes, and grants are never read.
/// </remarks>
public sealed class SqlLanguageAnalyzer : ILanguageAnalyzer
{
    /// <summary>The native kind of a table declaration.</summary>
    public const string TableNativeKind = "sql-table";

    /// <summary>The native kind of a column declaration.</summary>
    public const string ColumnNativeKind = "sql-column";

    /// <summary>The native kind of a row-level-security switch.</summary>
    public const string RowSecurityNativeKind = "sql-rls";

    /// <summary>The native kind of a row-level-security policy.</summary>
    public const string PolicyNativeKind = "sql-policy";

    private const string Id = "scout.analysis.sql";
    private const string Language = "sql";
    private const string PositionEncoding = "utf-16-code-units";

    private static readonly IReadOnlyList<string> Extensions = new ReadOnlyCollection<string>([".sql"]);

    private static readonly string AnalyzerVersion =
        typeof(SqlLanguageAnalyzer).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>Gets the stable identifier used by the SQL analyzer.</summary>
    public string AnalyzerId => Id;

    /// <summary>Gets the SQL language identifier.</summary>
    public string LanguageId => Language;

    /// <summary>Gets the repository extensions the SQL analyzer reads.</summary>
    public IReadOnlyList<string> SupportedFileExtensions => Extensions;

    /// <summary>Analyzes SQL files and returns only language-independent facts.</summary>
    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var builder = new ContributionBuilder(Id);
        var diagnostics = new List<AnalysisDiagnostic>();
        var files = repository.Files
            .Where(file => Extensions.Contains(Path.GetExtension(file.RelativePath), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File
                    .ReadAllTextAsync(Scout.Core.Repository.RepositoryPath.Resolve(repository.RootPath, file.RelativePath), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    "SQL_UNREADABLE_FILE",
                    $"SQL file '{file.RelativePath}' could not be read: {exception.Message}"));
                continue;
            }

            ReadFile(builder, file.RelativePath, file.ContentHash, content);
        }

        var status = diagnostics.Count == 0 ? KnownAnalyzerStatuses.Succeeded : KnownAnalyzerStatuses.Partial;
        return builder.Build(CreateMetadata(status), diagnostics);
    }

    private static void ReadFile(ContributionBuilder builder, string path, string contentHash, string content)
    {
        var fileId = builder.AddSourceFile(path, Language, contentHash);
        var lines = new LineIndex(content);
        var model = SqlSchemaReader.Read(content);

        SourceLocation Locate(int offset, int length) => lines.CreateLocation(path, offset, length, fileId);

        foreach (var table in model.Tables)
        {
            var location = Locate(table.Offset, table.Length);
            var tableId = builder.AddSymbol(
                StructuralId.Create(KnownEntityKinds.Symbol, path, $"table:{table.QualifiedName}"),
                table.Name,
                Language,
                KnownSymbolKinds.Type,
                TableNativeKind,
                table.QualifiedName,
                location,
                $"Table '{table.QualifiedName}' is created with {table.Columns.Count} column(s).");
            builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, tableId, location);

            foreach (var column in table.Columns)
            {
                AddColumn(builder, path, fileId, tableId, table.QualifiedName, column, Locate(column.Offset, column.Length));
            }
        }

        foreach (var added in model.AddedColumns)
        {
            var qualified = $"{added.Schema}.{added.Table}";
            var parentId = StructuralId.Create(KnownEntityKinds.Symbol, path, $"table:{qualified}");
            AddColumn(builder, path, fileId, parentId, qualified, added.Column, Locate(added.Column.Offset, added.Column.Length));
        }

        foreach (var security in model.RowSecurity)
        {
            var state = security.Enabled ? "enabled" : "disabled";
            var location = Locate(security.Offset, security.Length);
            var id = builder.AddSymbol(
                StructuralId.Create(KnownEntityKinds.Symbol, path, $"rls:{security.QualifiedTable}:{state}:{security.Offset}"),
                $"{security.Table} row level security {state}",
                Language,
                KnownSymbolKinds.Value,
                RowSecurityNativeKind,
                $"{security.QualifiedTable}.rls.{state}",
                location,
                $"Row level security is {state} on table '{security.QualifiedTable}'.");
            builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, id, location);
        }

        foreach (var policy in model.Policies)
        {
            var roles = policy.Roles.Count == 0 ? "all roles" : string.Join(", ", policy.Roles);
            var location = Locate(policy.Offset, policy.Length);
            var id = builder.AddSymbol(
                StructuralId.Create(KnownEntityKinds.Symbol, path, $"policy:{policy.QualifiedTable}:{policy.Name}"),
                policy.Name,
                Language,
                KnownSymbolKinds.Value,
                PolicyNativeKind,
                $"{policy.QualifiedTable}.policy.{policy.Name}",
                location,
                $"Policy '{policy.Name}' on table '{policy.QualifiedTable}' applies to {policy.Command.ToUpperInvariant()} for {roles}.");
            builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, id, location);
        }

        foreach (var item in model.NamedObjects)
        {
            var location = Locate(item.Offset, item.Length);
            var isRoutine = item.Kind is "function" or "procedure";
            var description = item.Table is null
                ? $"{item.Kind} '{item.QualifiedName}' is declared."
                : $"Index '{item.Name}' is declared on table '{item.Table}'.";
            var id = builder.AddSymbol(
                StructuralId.Create(KnownEntityKinds.Symbol, path, $"{item.Kind}:{item.QualifiedName}:{item.Table}"),
                item.Name,
                Language,
                isRoutine ? KnownSymbolKinds.Callable : item.Kind == "view" ? KnownSymbolKinds.Type : KnownSymbolKinds.Value,
                $"sql-{item.Kind}",
                item.QualifiedName,
                location,
                description);
            builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, id, location);
        }
    }

    private static void AddColumn(
        ContributionBuilder builder,
        string path,
        StructuralId fileId,
        StructuralId tableId,
        string qualifiedTable,
        SqlColumn column,
        SourceLocation location)
    {
        var type = column.Type.Length == 0 ? "unspecified type" : column.Type;
        var id = builder.AddSymbol(
            StructuralId.Create(KnownEntityKinds.Symbol, path, $"column:{qualifiedTable}.{column.Name}"),
            column.Name,
            Language,
            KnownSymbolKinds.Value,
            ColumnNativeKind,
            $"{qualifiedTable}.{column.Name}",
            location,
            $"Column '{column.Name}' ({type}) of table '{qualifiedTable}'.");
        builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, id, location);
        builder.AddRelationship(KnownRelationshipKinds.Contains, tableId, id, location);
    }

    private static AnalyzerRunMetadata CreateMetadata(string status) =>
        new(Id, "Scout SQL Analyzer", AnalyzerVersion, Language, status, DateTimeOffset.UtcNow, PositionEncoding);

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message) =>
        new(
            StructuralId.Create("diagnostic", Id, code, message),
            Id,
            KnownDiagnosticSeverities.Warning,
            code,
            message);
}

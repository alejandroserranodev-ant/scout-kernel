using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scout.Cli.Db;

/// <summary>What a live database reports about its own schema: names and switches only, never row data.</summary>
internal sealed record DatabaseSnapshot(
    [property: JsonPropertyName("tables")] IReadOnlyList<LiveTable> Tables,
    [property: JsonPropertyName("policies")] IReadOnlyList<LivePolicy> Policies,
    [property: JsonPropertyName("columns")] IReadOnlyList<LiveColumn> Columns)
{
    internal static DatabaseSnapshot FromJson(string json) =>
        JsonSerializer.Deserialize<DatabaseSnapshot>(json)
        ?? throw new InvalidOperationException("The database returned an empty schema snapshot.");
}

internal sealed record LiveTable(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("rls")] bool RowSecurity);

internal sealed record LivePolicy(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("table")] string Table,
    [property: JsonPropertyName("name")] string Name);

internal sealed record LiveColumn(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("table")] string Table,
    [property: JsonPropertyName("name")] string Name);

/// <summary>Reads a <see cref="DatabaseSnapshot"/>. The seam that keeps the comparison testable without a database.</summary>
internal interface IDatabaseSnapshotSource
{
    Task<DatabaseSnapshot> ReadAsync(CancellationToken cancellationToken);
}

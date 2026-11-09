using Microsoft.Data.Sqlite;
using Scout.Core.Retrieval;

namespace Scout.Retrieval.Sqlite;

/// <summary>
/// A machine-wide cache of dense vectors, keyed by model and by the hash of the embedded text.
/// </summary>
/// <remarks>
/// The cache lives outside any kernel on purpose: a kernel is replaced atomically on every refresh, and the cache
/// is what makes a refresh cheap. Because a vector depends only on its model and its text, it can be shared by
/// every repository on the machine and never goes stale.
/// </remarks>
public sealed class SqliteDenseEmbeddingCache : IDenseEmbeddingCache
{
    private const int SqliteVariableLimit = 500;

    private readonly string connectionString;

    /// <summary>
    /// Initializes the cache, creating its file if needed.
    /// </summary>
    /// <param name="databasePath">The path of the cache database.</param>
    public SqliteDenseEmbeddingCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ConnectionString;

        using var connection = Open();
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS vectors (
                model_id  TEXT NOT NULL,
                text_hash TEXT NOT NULL,
                vector    BLOB NOT NULL,
                PRIMARY KEY (model_id, text_hash)
            ) WITHOUT ROWID;
            """;
        create.ExecuteNonQuery();
    }

    /// <summary>
    /// Gets the default cache location for the current user.
    /// </summary>
    /// <returns>The path of the per-user cache database.</returns>
    public static string DefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Scout",
            "embedding-cache.db");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, float[]> GetMany(string modelId, IReadOnlyCollection<string> textHashes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(textHashes);

        var found = new Dictionary<string, float[]>(StringComparer.Ordinal);
        using var connection = Open();
        foreach (var batch in textHashes.Chunk(SqliteVariableLimit))
        {
            using var command = connection.CreateCommand();
            var names = batch.Select((_, index) => $"$h{index}").ToArray();
            command.CommandText =
                $"SELECT text_hash, vector FROM vectors WHERE model_id = $model AND text_hash IN ({string.Join(',', names)});";
            command.Parameters.AddWithValue("$model", modelId);
            for (var index = 0; index < batch.Length; index++)
            {
                command.Parameters.AddWithValue(names[index], batch[index]);
            }

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                found[reader.GetString(0)] = ToFloats((byte[])reader["vector"]);
            }
        }

        return found;
    }

    /// <inheritdoc />
    public void PutMany(string modelId, IReadOnlyDictionary<string, float[]> vectors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(vectors);

        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO vectors (model_id, text_hash, vector) VALUES ($model, $hash, $vector);";
        command.Parameters.AddWithValue("$model", modelId);
        var hash = command.Parameters.Add("$hash", SqliteType.Text);
        var vector = command.Parameters.Add("$vector", SqliteType.Blob);
        foreach (var (textHash, values) in vectors)
        {
            hash.Value = textHash;
            vector.Value = ToBytes(values);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static byte[] ToBytes(float[] values)
    {
        var bytes = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ToFloats(byte[] bytes)
    {
        var values = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}

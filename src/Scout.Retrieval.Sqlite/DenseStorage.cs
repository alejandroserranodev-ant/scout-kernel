using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Scout.Core.Retrieval;

namespace Scout.Retrieval.Sqlite;

/// <summary>
/// Persists and scans the dense vectors of a retrieval index.
/// </summary>
/// <remarks>
/// A vector is stored as one byte per component plus a scale, which is a quarter of its float size. For unit
/// vectors the rounding error is far below the gap between a good match and a poor one, and the index stays
/// small enough to live inside a kernel. The scan is exhaustive: a dense vector shares every dimension with every
/// other, so an inverted index would read all of them anyway.
/// </remarks>
internal static class DenseStorage
{
    private const int ScaleBytes = sizeof(float);

    internal static void Write(SqliteConnection connection, RetrievalIndex index)
    {
        if (index.Dense is not { } dense)
        {
            return;
        }

        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = RetrievalSchema.CreateDense;
            schema.ExecuteNonQuery();
        }

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO dense_vectors (card_rowid, vector) VALUES ($rowid, $vector);";
        var rowId = command.Parameters.Add("$rowid", SqliteType.Integer);
        var vector = command.Parameters.Add("$vector", SqliteType.Blob);
        for (var position = 0; position < dense.Vectors.Count; position++)
        {
            rowId.Value = position + 1;
            vector.Value = Encode(dense.Vectors[position]);
            command.ExecuteNonQuery();
        }
    }

    internal static IReadOnlyDictionary<string, string> MetadataFor(RetrievalIndex index) =>
        index.Dense is not { } dense
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RetrievalSchema.MetadataDenseModelId] = dense.ModelId,
                [RetrievalSchema.MetadataDenseDimensions] = dense.Dimensions.ToString(CultureInfo.InvariantCulture)
            };

    /// <summary>Ranks the cards of a kind by cosine similarity to a query vector.</summary>
    internal static List<(long RowId, double Similarity)> Scan(
        SqliteConnection connection,
        string? kind,
        float[] query,
        int depth)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.card_rowid, d.vector
            FROM dense_vectors d
            JOIN cards c ON c.rowid = d.card_rowid
            WHERE $kind IS NULL OR c.kind = $kind;
            """;
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);

        var scored = new List<(long, double)>();
        var buffer = new float[query.Length];
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var blob = (byte[])reader["vector"];
            if (blob.Length != ScaleBytes + query.Length)
            {
                throw new InvalidOperationException(
                    "The dense vectors in this index do not have the dimension of the query model.");
            }

            Decode(blob, buffer);
            scored.Add((reader.GetInt64(0), DenseVectors.Dot(buffer, query)));
        }

        return scored
            .OrderByDescending(entry => entry.Item2)
            .ThenBy(entry => entry.Item1)
            .Take(depth)
            .ToList();
    }

    internal static byte[] Encode(float[] vector)
    {
        var largest = 0f;
        foreach (var value in vector)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        var scale = largest > 0 ? largest / sbyte.MaxValue : 1f;
        var blob = new byte[ScaleBytes + vector.Length];
        BinaryPrimitives.WriteSingleLittleEndian(blob, scale);
        for (var index = 0; index < vector.Length; index++)
        {
            blob[ScaleBytes + index] = (byte)(sbyte)Math.Clamp(
                (int)Math.Round(vector[index] / scale),
                sbyte.MinValue,
                sbyte.MaxValue);
        }

        return blob;
    }

    internal static void Decode(byte[] blob, float[] destination)
    {
        var scale = BinaryPrimitives.ReadSingleLittleEndian(blob);
        for (var index = 0; index < destination.Length; index++)
        {
            destination[index] = (sbyte)blob[ScaleBytes + index] * scale;
        }
    }
}

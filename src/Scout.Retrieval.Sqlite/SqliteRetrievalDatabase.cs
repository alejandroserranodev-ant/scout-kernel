using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Scout.Core.Retrieval;

namespace Scout.Retrieval.Sqlite;

/// <summary>
/// A Scout retrieval index stored as a single portable SQLite database.
/// </summary>
/// <remarks>
/// <para>
/// A database rather than a bespoke file because the index is meant to be read by tools Scout does not own.
/// Every language has a SQLite driver, the file is self-describing, and an agent holding a database tool can
/// query the corpus directly instead of going through Scout at all.
/// </para>
/// <para>
/// It carries the fingerprint of the repository it was built from, so a caller can tell a current index from
/// one describing code that has since moved on.
/// </para>
/// </remarks>
public sealed class SqliteRetrievalDatabase : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteConnection connection;
    private readonly IQueryEmbedder embedder;
    private IDenseEmbeddingModel? denseModel;

    private SqliteRetrievalDatabase(
        SqliteConnection connection,
        IQueryEmbedder embedder,
        string modelId,
        string modelKind,
        int cardCount,
        string? sourceFingerprint,
        string? denseModelId,
        IDenseEmbeddingModel? denseModel)
    {
        this.connection = connection;
        this.embedder = embedder;
        ModelId = modelId;
        ModelKind = modelKind;
        CardCount = cardCount;
        SourceFingerprint = sourceFingerprint;
        DenseModelId = denseModelId;
        this.denseModel = denseModel;
    }

    /// <summary>
    /// Gets the dense model whose vectors this database carries, or <see langword="null"/> when it is lexical only.
    /// </summary>
    public string? DenseModelId { get; }

    /// <summary>Gets the identifier of the model that built the space.</summary>
    public string ModelId { get; }

    /// <summary>Gets what the space matches. See <see cref="KnownEmbeddingKinds"/>.</summary>
    public string ModelKind { get; }

    /// <summary>Gets the number of indexed cards.</summary>
    public int CardCount { get; }

    /// <summary>Gets the fingerprint of the repository the index was built from, when recorded.</summary>
    public string? SourceFingerprint { get; }

    /// <summary>
    /// Writes an index to a new database file.
    /// </summary>
    /// <param name="databasePath">The database path. Any existing file is replaced.</param>
    /// <param name="index">The index to persist.</param>
    /// <param name="sourceFingerprint">The fingerprint of the repository the index describes.</param>
    public static void Write(string databasePath, RetrievalIndex index, string? sourceFingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(index);

        var fullPath = Path.GetFullPath(databasePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        using var connection = OpenConnection(fullPath, readOnly: false);
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = RetrievalSchema.Create;
            schema.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        WriteMetadata(connection, index, sourceFingerprint);
        WriteVocabulary(connection, index);
        WriteCards(connection, index);
        WriteEdges(connection, index);
        DenseStorage.Write(connection, index);
        transaction.Commit();
    }

    /// <summary>
    /// Opens an existing database for searching, choosing the dense model from what the database itself records.
    /// </summary>
    /// <param name="databasePath">The database path.</param>
    /// <param name="model">The model that must match the one recorded in the database.</param>
    /// <param name="denseModelFor">
    /// Called with the dense model identifier recorded in the database, only when it has one. It returns the model
    /// that can embed a query into that space, or <see langword="null"/> when this machine has none.
    /// </param>
    /// <returns>The opened database.</returns>
    public static SqliteRetrievalDatabase Open(
        string databasePath,
        IEmbeddingModel model,
        Func<string, IDenseEmbeddingModel?> denseModelFor)
    {
        ArgumentNullException.ThrowIfNull(denseModelFor);

        var database = Open(databasePath, model);
        if (database.DenseModelId is not null)
        {
            database.denseModel = denseModelFor(database.DenseModelId);
        }

        return database;
    }

    /// <summary>
    /// Opens an existing database for searching.
    /// </summary>
    /// <param name="databasePath">The database path.</param>
    /// <param name="model">The model that must match the one recorded in the database.</param>
    /// <param name="denseModel">
    /// An optional dense model used to embed queries. It is used only when the database carries vectors from the same
    /// model; otherwise the database answers lexically and <see cref="SearchAsync"/> says why.
    /// </param>
    /// <returns>The opened database.</returns>
    /// <exception cref="InvalidOperationException">
    /// The database was written by a different schema version or a different model, so its vectors cannot be
    /// compared against a query this model would produce.
    /// </exception>
    public static SqliteRetrievalDatabase Open(
        string databasePath,
        IEmbeddingModel model,
        IDenseEmbeddingModel? denseModel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(model);

        var fullPath = Path.GetFullPath(databasePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Retrieval index not found: {fullPath}", fullPath);
        }

        var connection = OpenConnection(fullPath, readOnly: true);
        try
        {
            var metadata = ReadMetadata(connection);
            var schemaVersion = metadata.GetValueOrDefault(RetrievalSchema.MetadataSchemaVersion);
            if (!string.Equals(schemaVersion, RetrievalSchema.Version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Retrieval index schema '{schemaVersion ?? "unknown"}' cannot be read by this version, which expects " +
                    $"'{RetrievalSchema.Version}'. The kernel was built by a different Scout version: recompile it with `scout compile` (or `scout update`).");
            }

            var modelId = metadata.GetValueOrDefault(RetrievalSchema.MetadataModelId) ?? "unknown";
            if (!string.Equals(modelId, model.ModelId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Retrieval index was built by model '{modelId}', which is not the model being queried ('{model.ModelId}').");
            }

            return new SqliteRetrievalDatabase(
                connection,
                model.Restore(ReadVocabulary(connection)),
                modelId,
                metadata.GetValueOrDefault(RetrievalSchema.MetadataModelKind) ?? KnownEmbeddingKinds.Lexical,
                int.Parse(
                    metadata.GetValueOrDefault(RetrievalSchema.MetadataCardCount) ?? "0",
                    CultureInfo.InvariantCulture),
                metadata.GetValueOrDefault(RetrievalSchema.MetadataSourceFingerprint),
                metadata.GetValueOrDefault(RetrievalSchema.MetadataDenseModelId),
                denseModel);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Searches the index.
    /// </summary>
    /// <param name="query">The caller's intent, in their own words.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <param name="kind">An optional card kind to restrict the search to.</param>
    /// <returns>The ranked hits, which is empty when the query shares no term with the corpus.</returns>
    public IReadOnlyList<RetrievalHit> Search(string? query, int limit, string? kind = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var vector = embedder.Embed(query);
        if (vector.IsEmpty)
        {
            return [];
        }

        using var transaction = connection.BeginTransaction();
        CreateQueryTerms(vector);
        var candidates = ReadCandidates(kind);
        transaction.Rollback();
        return RetrievalRanking.Rank(candidates, limit, query);
    }

    /// <summary>
    /// Finds every card of a given kind whose name matches exactly (case-insensitively), without ranking or
    /// embedding a query. For resolving a specific symbol or file to its source location, which does not need
    /// (and should not pay for) similarity search.
    /// </summary>
    /// <param name="kind">The card kind. See <see cref="KnownCardKinds"/>.</param>
    /// <param name="name">The exact name to match.</param>
    /// <returns>Every matching card, ordered by path then name for a deterministic result.</returns>
    public IReadOnlyList<RetrievalCard> FindByExactName(string kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, relative_path, summary, importance, detail, facets, related
            FROM cards
            WHERE kind = $kind AND name = $name COLLATE NOCASE
            ORDER BY relative_path, name;
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$name", name);

        var matches = new List<RetrievalCard>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            matches.Add(ReadCard(reader));
        }

        return matches;
    }

    /// <inheritdoc />
    public void Dispose() => connection.Dispose();

    /// <summary>
    /// Searches the index, fusing the semantic ranking with the lexical one when the index carries dense vectors
    /// and a matching model is available.
    /// </summary>
    /// <remarks>
    /// Semantic search is an addition, never a requirement. When the index has no dense vectors, no model was
    /// supplied, the model differs from the one that built the vectors, or the model cannot be reached, the
    /// answer is the lexical one and <see cref="RetrievalSearchResult.Notice"/> says what was missing.
    /// </remarks>
    /// <param name="query">The caller's intent, in their own words.</param>
    /// <param name="limit">The maximum number of hits to return.</param>
    /// <param name="kind">An optional card kind to restrict the search to.</param>
    /// <param name="cancellationToken">A token that cancels the query embedding.</param>
    /// <returns>The hits, how they were produced, and why a semantic ranking was not used, if it was not.</returns>
    public async Task<RetrievalSearchResult> SearchAsync(
        string? query,
        int limit,
        string? kind = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (DenseModelId is null)
        {
            return new RetrievalSearchResult(Search(query, limit, kind), KnownSearchModes.Lexical);
        }

        if (denseModel is null)
        {
            return Lexical(
                query,
                limit,
                kind,
                $"This index also carries '{DenseModelId}' semantic vectors; no embedding model was given, so only the wording of the query was matched.");
        }

        if (!string.Equals(denseModel.ModelId, DenseModelId, StringComparison.Ordinal))
        {
            return Lexical(
                query,
                limit,
                kind,
                $"This index carries '{DenseModelId}' semantic vectors, which '{denseModel.ModelId}' cannot be compared with, so only the wording of the query was matched.");
        }

        float[] queryVector;
        try
        {
            var embedded = await denseModel.EmbedAsync([query ?? string.Empty], cancellationToken).ConfigureAwait(false);
            queryVector = embedded[0];
        }
        catch (DenseEmbeddingUnavailableException exception)
        {
            return Lexical(query, limit, kind, $"{exception.Message} Only the wording of the query was matched.");
        }

        var lexical = Search(query, RetrievalRanking.FusionDepth, kind);
        var semantic = ReadSemanticCandidates(kind, queryVector);
        return new RetrievalSearchResult(
            RetrievalRanking.Fuse(lexical, semantic, limit),
            KnownSearchModes.Hybrid);
    }

    private static SqliteConnection OpenConnection(string fullPath, bool readOnly)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void WriteMetadata(SqliteConnection connection, RetrievalIndex index, string? sourceFingerprint)
    {
        var entries = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [RetrievalSchema.MetadataSchemaVersion] = RetrievalSchema.Version,
            [RetrievalSchema.MetadataModelId] = index.ModelId,
            [RetrievalSchema.MetadataModelKind] = index.ModelKind,
            [RetrievalSchema.MetadataCardCount] = index.Cards.Count.ToString(CultureInfo.InvariantCulture),
            [RetrievalSchema.MetadataSourceFingerprint] = sourceFingerprint
        };
        foreach (var (denseKey, denseValue) in DenseStorage.MetadataFor(index))
        {
            entries[denseKey] = denseValue;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO index_metadata (key, value) VALUES ($key, $value);";
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var value = command.Parameters.Add("$value", SqliteType.Text);
        foreach (var (entryKey, entryValue) in entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (entryValue is null)
            {
                continue;
            }

            key.Value = entryKey;
            value.Value = entryValue;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteVocabulary(SqliteConnection connection, RetrievalIndex index)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO vocabulary (dimension, term, idf) VALUES ($dimension, $term, $idf);";
        var dimension = command.Parameters.Add("$dimension", SqliteType.Integer);
        var term = command.Parameters.Add("$term", SqliteType.Text);
        var idf = command.Parameters.Add("$idf", SqliteType.Real);
        foreach (var entry in index.Vocabulary)
        {
            dimension.Value = entry.Dimension;
            term.Value = entry.Term;
            idf.Value = entry.InverseDocumentFrequency;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteEdges(SqliteConnection connection, RetrievalIndex index)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO edges (source_id, target_id, kind) VALUES ($source, $target, $kind);";
        var source = command.Parameters.Add("$source", SqliteType.Text);
        var target = command.Parameters.Add("$target", SqliteType.Text);
        var kind = command.Parameters.Add("$kind", SqliteType.Text);
        foreach (var edge in index.Edges)
        {
            source.Value = edge.SourceId;
            target.Value = edge.TargetId;
            kind.Value = edge.Kind;
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Finds a card by its id.</summary>
    /// <param name="cardId">The card id.</param>
    /// <returns>The card, or <see langword="null"/> when no card has that id.</returns>
    public RetrievalCard? FindById(string cardId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, name, relative_path, summary, importance, detail, facets, related
            FROM cards WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", cardId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadCard(reader) : null;
    }

    /// <summary>Reads the edges that leave (<paramref name="outgoing"/>) or arrive at a card, in a stable order.</summary>
    /// <param name="cardId">The card id.</param>
    /// <param name="outgoing"><see langword="true"/> for edges the card starts, <see langword="false"/> for edges that point to it.</param>
    /// <returns>The edges.</returns>
    public IReadOnlyList<RetrievalEdge> ReadEdges(string cardId, bool outgoing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cardId);

        using var command = connection.CreateCommand();
        command.CommandText = outgoing
            ? "SELECT source_id, target_id, kind FROM edges WHERE source_id = $id ORDER BY kind, target_id;"
            : "SELECT source_id, target_id, kind FROM edges WHERE target_id = $id ORDER BY kind, source_id;";
        command.Parameters.AddWithValue("$id", cardId);
        var edges = new List<RetrievalEdge>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            edges.Add(new RetrievalEdge(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return edges;
    }

    private static void WriteCards(SqliteConnection connection, RetrievalIndex index)
    {
        using var cardCommand = connection.CreateCommand();
        cardCommand.CommandText = """
            INSERT INTO cards (rowid, id, kind, name, relative_path, summary, importance, detail, facets, related)
            VALUES ($rowid, $id, $kind, $name, $path, $summary, $importance, $detail, $facets, $related);
            """;
        var rowId = cardCommand.Parameters.Add("$rowid", SqliteType.Integer);
        var id = cardCommand.Parameters.Add("$id", SqliteType.Text);
        var kind = cardCommand.Parameters.Add("$kind", SqliteType.Text);
        var name = cardCommand.Parameters.Add("$name", SqliteType.Text);
        var path = cardCommand.Parameters.Add("$path", SqliteType.Text);
        var summary = cardCommand.Parameters.Add("$summary", SqliteType.Text);
        var importance = cardCommand.Parameters.Add("$importance", SqliteType.Real);
        var detail = cardCommand.Parameters.Add("$detail", SqliteType.Text);
        var facets = cardCommand.Parameters.Add("$facets", SqliteType.Text);
        var related = cardCommand.Parameters.Add("$related", SqliteType.Text);

        using var postingCommand = connection.CreateCommand();
        postingCommand.CommandText =
            "INSERT INTO postings (dimension, card_rowid, weight) VALUES ($dimension, $rowid, $weight);";
        var postingDimension = postingCommand.Parameters.Add("$dimension", SqliteType.Integer);
        var postingRowId = postingCommand.Parameters.Add("$rowid", SqliteType.Integer);
        var postingWeight = postingCommand.Parameters.Add("$weight", SqliteType.Real);

        for (var index2 = 0; index2 < index.Cards.Count; index2++)
        {
            var card = index.Cards[index2];
            var currentRowId = index2 + 1;
            rowId.Value = currentRowId;
            id.Value = card.Id;
            kind.Value = card.Kind;
            name.Value = card.Name;
            path.Value = card.RelativePath;
            summary.Value = card.Summary;
            importance.Value = card.Importance;
            detail.Value = (object?)card.Detail ?? DBNull.Value;
            facets.Value = JsonSerializer.Serialize(card.Facets, JsonOptions);
            related.Value = JsonSerializer.Serialize(card.Related, JsonOptions);
            cardCommand.ExecuteNonQuery();

            postingRowId.Value = currentRowId;
            foreach (var component in index.Vectors[index2].Components)
            {
                postingDimension.Value = component.Dimension;
                postingWeight.Value = component.Weight;
                postingCommand.ExecuteNonQuery();
            }
        }
    }

    private static Dictionary<string, string> ReadMetadata(SqliteConnection connection)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM index_metadata;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            metadata[reader.GetString(0)] = reader.GetString(1);
        }

        return metadata;
    }

    private static List<EmbeddingTerm> ReadVocabulary(SqliteConnection connection)
    {
        var vocabulary = new List<EmbeddingTerm>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT dimension, term, idf FROM vocabulary ORDER BY dimension;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            vocabulary.Add(new EmbeddingTerm(reader.GetString(1), reader.GetInt32(0), reader.GetDouble(2)));
        }

        return vocabulary;
    }

    // The query lives in a temporary table so the dot product is computed by the join, touching only the
    // postings of terms the query actually contains.
    private void CreateQueryTerms(EmbeddingVector vector)
    {
        using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TEMP TABLE IF NOT EXISTS query_terms (
                dimension INTEGER PRIMARY KEY,
                weight    REAL NOT NULL
            );
            DELETE FROM query_terms;
            """;
        create.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO query_terms (dimension, weight) VALUES ($dimension, $weight);";
        var dimension = insert.Parameters.Add("$dimension", SqliteType.Integer);
        var weight = insert.Parameters.Add("$weight", SqliteType.Real);
        foreach (var component in vector.Components)
        {
            dimension.Value = component.Dimension;
            weight.Value = component.Weight;
            insert.ExecuteNonQuery();
        }
    }

    private RetrievalSearchResult Lexical(string? query, int limit, string? kind, string notice) =>
        new(Search(query, limit, kind), KnownSearchModes.Lexical, notice);

    private static RetrievalCard ReadCard(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetDouble(5),
            JsonSerializer.Deserialize<RetrievalFacet[]>(reader.GetString(7), JsonOptions),
            JsonSerializer.Deserialize<string[]>(reader.GetString(8), JsonOptions),
            reader.IsDBNull(6) ? null : reader.GetString(6));

    // Scores every dense vector first and only then reads the cards of the best ones, so a query never pays to
    // materialize thousands of cards it is about to discard.
    private List<(RetrievalCard Card, double Similarity)> ReadSemanticCandidates(string? kind, float[] queryVector)
    {
        var best = DenseStorage.Scan(connection, kind, queryVector, RetrievalRanking.FusionDepth);
        var candidates = new List<(RetrievalCard, double)>(best.Count);
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, kind, name, relative_path, summary, importance, detail, facets, related " +
            "FROM cards WHERE rowid = $rowid;";
        var rowId = command.Parameters.Add("$rowid", SqliteType.Integer);
        foreach (var (id, similarity) in best)
        {
            rowId.Value = id;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                candidates.Add((ReadCard(reader), similarity));
            }
        }

        return candidates;
    }

    private List<(RetrievalCard Card, double Similarity)> ReadCandidates(string? kind)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.id, c.kind, c.name, c.relative_path, c.summary, c.importance, c.detail, c.facets, c.related,
                   SUM(p.weight * q.weight) AS similarity
            FROM postings p
            JOIN query_terms q ON q.dimension = p.dimension
            JOIN cards c ON c.rowid = p.card_rowid
            WHERE $kind IS NULL OR c.kind = $kind
            GROUP BY p.card_rowid;
            """;
        command.Parameters.AddWithValue("$kind", (object?)kind ?? DBNull.Value);

        var candidates = new List<(RetrievalCard, double)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            candidates.Add((ReadCard(reader), reader.GetDouble(9)));
        }

        return candidates;
    }
}

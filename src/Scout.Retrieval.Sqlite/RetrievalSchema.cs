namespace Scout.Retrieval.Sqlite;

/// <summary>
/// The physical layout of a Scout retrieval database.
/// </summary>
/// <remarks>
/// The schema is an inverted index, not a table of dense vectors. For a corpus whose vocabulary is built from
/// identifiers this is both smaller and faster: a query touches only the postings of the terms it actually
/// contains, so search cost follows the query rather than the size of the repository. A dense model can still
/// be stored here by treating every dimension as a posting.
/// </remarks>
internal static class RetrievalSchema
{
    /// <summary>The schema version recorded in the database, so an incompatible index is rejected, not misread.</summary>
    internal const string Version = "1";

    internal const string Create = """
        PRAGMA journal_mode = OFF;
        PRAGMA synchronous = OFF;

        CREATE TABLE index_metadata (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE cards (
            rowid         INTEGER PRIMARY KEY,
            id            TEXT NOT NULL UNIQUE,
            kind          TEXT NOT NULL,
            name          TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            summary       TEXT NOT NULL,
            importance    REAL NOT NULL,
            detail        TEXT NULL,
            facets        TEXT NOT NULL,
            related       TEXT NOT NULL
        );

        CREATE INDEX ix_cards_kind ON cards (kind);

        CREATE TABLE vocabulary (
            dimension INTEGER PRIMARY KEY,
            term      TEXT NOT NULL UNIQUE,
            idf       REAL NOT NULL
        );

        CREATE TABLE postings (
            dimension  INTEGER NOT NULL,
            card_rowid INTEGER NOT NULL,
            weight     REAL NOT NULL,
            PRIMARY KEY (dimension, card_rowid)
        ) WITHOUT ROWID;
        """;

    // Present only when the index carries dense vectors. An index without them has no such table, so a reader
    // that predates dense vectors, or a query without a dense model, still reads every index.
    internal const string CreateDense = """
        CREATE TABLE dense_vectors (
            card_rowid INTEGER PRIMARY KEY,
            vector     BLOB NOT NULL
        );
        """;

    internal const string MetadataDenseModelId = "denseModelId";
    internal const string MetadataDenseDimensions = "denseDimensions";
    internal const string MetadataSchemaVersion = "schemaVersion";
    internal const string MetadataModelId = "modelId";
    internal const string MetadataModelKind = "modelKind";
    internal const string MetadataCardCount = "cardCount";
    internal const string MetadataSourceFingerprint = "sourceFingerprint";
}

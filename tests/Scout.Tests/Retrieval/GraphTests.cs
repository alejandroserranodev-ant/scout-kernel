using System.Text.Json;
using Microsoft.Data.Sqlite;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class GraphTests
{
    private static async Task<string> CompileAsync(TemporaryRepository repository, string name)
    {
        var kernel = Path.Combine(repository.WorkspacePath, name);
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);
        return kernel;
    }

    private static RetrievalCard Card(SqliteRetrievalDatabase database, string name) =>
        database.FindByExactName(KnownCardKinds.Symbol, name).Concat(database.FindByExactName(KnownCardKinds.Contract, name)).First();

    [Fact]
    public async Task TheIndexRecordsTypedDirectedEdges()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "graph-edges");
        var kernel = await CompileAsync(repository, "graph.scout").ConfigureAwait(true);
        using var database = SqliteRetrievalDatabase.Open(Path.Combine(kernel, "retrieval", "index.db"), new LexicalEmbeddingModel());

        var handler = Card(database, "CreateInvoiceHandler");
        var baseType = Card(database, "InvoiceHandlerBase");
        var store = Card(database, "InMemoryInvoiceStore");
        var storeInterface = Card(database, "IInvoiceStore");

        Assert.Contains(database.ReadEdges(handler.Id, outgoing: true), edge => edge.TargetId == baseType.Id && edge.Kind == "inherits");
        Assert.Contains(database.ReadEdges(baseType.Id, outgoing: false), edge => edge.SourceId == handler.Id && edge.Kind == "inherits");
        Assert.Contains(database.ReadEdges(store.Id, outgoing: true), edge => edge.TargetId == storeInterface.Id && edge.Kind == "implements");
    }

    [Fact]
    public async Task AnOlderIndexSchemaSaysToRecompile()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "graph-old-schema");
        var kernel = await CompileAsync(repository, "old.scout").ConfigureAwait(true);
        var indexPath = Path.Combine(kernel, "retrieval", "index.db");
        using (var connection = new SqliteConnection($"Data Source={indexPath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE index_metadata SET value = '1' WHERE key = 'schemaVersion';";
            command.ExecuteNonQuery();
        }

        var exception = Assert.Throws<InvalidOperationException>(() => SqliteRetrievalDatabase.Open(indexPath, new LexicalEmbeddingModel()));

        Assert.Contains("recompile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NeighborsReportsTheKindAndDirectionOfEachHop()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "graph-neighbors");
        var kernel = await CompileAsync(repository, "neighbors.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        var exit = await NeighborsCommand
            .RunAsync(["CreateInvoiceHandler", "--kernel", kernel, "--explain", "InvoiceHandlerBase", "--json"], output, new StringWriter())
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.True(answer.GetProperty("Connected").GetBoolean());
        var step = answer.GetProperty("Steps")[0];
        Assert.Equal("inherits", step.GetProperty("Kind").GetString());
        Assert.Equal("out", step.GetProperty("Direction").GetString());
    }

    [Fact]
    public async Task NeighborsListsIncomingEdgesWithTheirDirection()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "graph-incoming");
        var kernel = await CompileAsync(repository, "incoming.scout").ConfigureAwait(true);

        using var output = new StringWriter();
        await NeighborsCommand.RunAsync(["InvoiceHandlerBase", "--kernel", kernel], output, new StringWriter()).ConfigureAwait(true);

        Assert.Contains("inherits <- CreateInvoiceHandler", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheKernelReportIsShortLinkedAndDeterministic()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "graph-report");
        var first = await CompileAsync(repository, "report-1.scout").ConfigureAwait(true);
        var second = await CompileAsync(repository, "report-2.scout").ConfigureAwait(true);

        var report = await File.ReadAllTextAsync(Path.Combine(first, "KERNEL_REPORT.md")).ConfigureAwait(true);

        Assert.InRange(report.Split('\n').Length, 1, 101);
        Assert.Contains("## Most depended-on declarations", report, StringComparison.Ordinal);
        Assert.Contains("InvoiceHandlerBase", report, StringComparison.Ordinal);
        Assert.Contains("KERNEL_REPORT.md", await File.ReadAllTextAsync(Path.Combine(first, "KERNEL.md")).ConfigureAwait(true), StringComparison.Ordinal);
        Assert.Equal(report, await File.ReadAllTextAsync(Path.Combine(second, "KERNEL_REPORT.md")).ConfigureAwait(true));
    }
}

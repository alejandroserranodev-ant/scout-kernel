using Scout.Analysis.CSharp;
using Scout.Analysis.Sql;
using Scout.Cli.Commands;
using Scout.Cli.Db;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Sql;

public sealed class SqlTests : IDisposable
{
    private const string Migration = """
        -- orders schema; a comment with a ; semicolon must not split a statement
        create table public.orders (
          id uuid primary key default gen_random_uuid(),
          customer_email text not null,
          total numeric(12, 2) default 0 check (total >= 0),
          constraint orders_total_positive check (total >= 0)
        );
        /* block comment; create table public.ghost (id int); */
        create table if not exists "public"."order_items" (id int, order_id uuid references public.orders(id));
        alter table public.orders add column if not exists note text;
        alter table public.orders enable row level security;
        create policy "orders readable" on public.orders for select to authenticated, anon using (true);
        create or replace function public.touch() returns trigger as $$
        begin
          perform 1; -- body; with semicolons
          return new;
        end;
        $$ language plpgsql;
        create unique index orders_email_idx on public.orders (customer_email);
        create view order_totals as select 1;
        """;

    private readonly string workspace = Path.Combine(Path.GetTempPath(), "Scout.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public void ReaderFindsTablesColumnsPoliciesAndSwitches()
    {
        var model = SqlSchemaReader.Read(Migration);

        Assert.Equal(["public.orders", "public.order_items"], model.Tables.Select(t => t.QualifiedName));
        Assert.Equal(["id", "customer_email", "total"], model.Tables[0].Columns.Select(c => c.Name));
        Assert.Equal("numeric(12, 2)", model.Tables[0].Columns[2].Type);
        Assert.Equal("note", Assert.Single(model.AddedColumns).Column.Name);
        Assert.True(Assert.Single(model.RowSecurity).Enabled);

        var policy = Assert.Single(model.Policies);
        Assert.Equal("orders readable", policy.Name);
        Assert.Equal("select", policy.Command);
        Assert.Equal(["authenticated", "anon"], policy.Roles);

        Assert.Equal(["function", "index", "view"], model.NamedObjects.Select(o => o.Kind).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void CommentsAndDollarQuotedBodiesNeverProduceDeclarations()
    {
        var model = SqlSchemaReader.Read(Migration);

        Assert.DoesNotContain(model.Tables, table => table.Name == "ghost");
    }

    [Fact]
    public void OffsetsPointAtTheOriginalText()
    {
        var model = SqlSchemaReader.Read(Migration);

        Assert.StartsWith("create table public.orders", Migration[model.Tables[0].Offset..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompiledKernelAnswersQuestionsAboutThePolicy()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "sql-compile");
        Directory.CreateDirectory(Path.Combine(repository.RootPath, "migrations"));
        await File.WriteAllTextAsync(Path.Combine(repository.RootPath, "migrations", "001_orders.sql"), Migration).ConfigureAwait(true);
        var kernel = Path.Combine(repository.WorkspacePath, "repo.scout");
        await new ScoutCompiler([new CSharpLanguageAnalyzer(), new SqlLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await QueryCommand.RunAsync(["orders readable policy", "--kernel", kernel, "--json"], output, error).ConfigureAwait(true);

        Assert.Equal(0, exit);
        Assert.Contains("orders readable", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StaticCheckFlagsTablesWithoutRowSecurity()
    {
        var findings = SchemaChecker.Check([SqlSchemaReader.Read(Migration)], live: null);

        var finding = Assert.Single(findings, f => f.Code == FindingCodes.TableWithoutRowSecurity);
        Assert.Equal("public.order_items", finding.Subject);
        Assert.DoesNotContain(findings, f => f.Subject == "public.orders" && f.Code == FindingCodes.TableWithoutRowSecurity);
    }

    [Fact]
    public void LiveCheckReportsDriftBothWays()
    {
        var live = DatabaseSnapshot.FromJson("""
            {
              "tables": [
                { "schema": "public", "name": "orders", "rls": false },
                { "schema": "public", "name": "legacy_table", "rls": true },
                { "schema": "auth", "name": "users", "rls": true }
              ],
              "policies": [ { "schema": "public", "table": "orders", "name": "surprise" } ],
              "columns": [
                { "schema": "public", "table": "orders", "name": "id" },
                { "schema": "public", "table": "orders", "name": "customer_email" },
                { "schema": "public", "table": "orders", "name": "extra" }
              ]
            }
            """);

        var codes = SchemaChecker.Check([SqlSchemaReader.Read(Migration)], live).Select(f => f.Code).ToHashSet();

        Assert.Contains(FindingCodes.RowSecurityDisabledInDatabase, codes);
        Assert.Contains(FindingCodes.TableMissingInDatabase, codes);
        Assert.Contains(FindingCodes.TableNotDeclared, codes);
        Assert.Contains(FindingCodes.PolicyMissingInDatabase, codes);
        Assert.Contains(FindingCodes.PolicyNotDeclared, codes);
        Assert.Contains(FindingCodes.ColumnMissingInDatabase, codes);
        Assert.Contains(FindingCodes.ColumnNotDeclared, codes);
        Assert.DoesNotContain(SchemaChecker.Check([SqlSchemaReader.Read(Migration)], live), f => f.Subject.StartsWith("auth.", StringComparison.Ordinal));
    }

    private async Task<string> RunCheckAsync(Func<string, string?> environment, Func<string, string, IDatabaseSnapshotSource>? factory = null)
    {
        var repository = Path.Combine(workspace, "check");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "001.sql"), Migration).ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await DbCommand.RunAsync(["check", repository], output, error, environment, factory, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        return output.ToString();
    }

    [Fact]
    public async Task WithoutAConnectionItStillReportsStaticFindingsAndSaysWhy()
    {
        var text = await RunCheckAsync(_ => null).ConfigureAwait(true);

        Assert.Contains("not checked: environment variable SCOUT_DB_URL is not set", text, StringComparison.Ordinal);
        Assert.Contains(FindingCodes.TableWithoutRowSecurity, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissingPsqlClientDegradesToStaticFindingsAndNeverLeaksTheConnection()
    {
        const string secret = "postgres://user:hunter2@localhost/db";
        var text = await RunCheckAsync(_ => secret, (_, _) => new ThrowingSource()).ConfigureAwait(true);

        Assert.Contains("not checked:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.Contains(FindingCodes.TableWithoutRowSecurity, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithASnapshotTheLiveFindingsAreIncluded()
    {
        var text = await RunCheckAsync(_ => "conn", (_, _) => new FixedSource()).ConfigureAwait(true);

        Assert.Contains("live database: checked", text, StringComparison.Ordinal);
        Assert.Contains(FindingCodes.RowSecurityDisabledInDatabase, text, StringComparison.Ordinal);
    }

    private sealed class ThrowingSource : IDatabaseSnapshotSource
    {
        public Task<DatabaseSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Could not run 'psql': not found");
    }

    private sealed class FixedSource : IDatabaseSnapshotSource
    {
        public Task<DatabaseSnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(DatabaseSnapshot.FromJson(
                """{ "tables": [ { "schema": "public", "name": "orders", "rls": false } ], "policies": [], "columns": [] }"""));
    }
}

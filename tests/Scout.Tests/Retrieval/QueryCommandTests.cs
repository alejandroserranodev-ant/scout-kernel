using System.Text.Json;
using Scout.Analysis.Markdown;
using Scout.Analysis.TypeScript;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class QueryCommandTests
{
    [Fact]
    public async Task JsonAnswerSurvivesASectionAboutTwoTopics()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        repository.WriteFile("docs/guia.md", "# Diseño y pruebas\n");
        var kernel = Path.Combine(repository.WorkspacePath, "docs.scout");
        await new ScoutCompiler([new TypeScriptLanguageAnalyzer(), new MarkdownLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await QueryCommand.RunAsync(["Diseño y pruebas", "--kernel", kernel, "--json"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        var hit = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("Hits")[0];
        Assert.Equal("Diseño y pruebas", hit.GetProperty("Name").GetString());
        var topics = hit.GetProperty("Facets").GetProperty("topic").GetString()!.Split(", ").Order(StringComparer.Ordinal);
        Assert.Equal(["architecture", "testing"], topics);
    }

    [Fact]
    public async Task ALexicalKernelIsQueriedWithoutAnyNoticeOrModel()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalReactTextRepository", "MinimalReactTextRepository");
        var kernel = Path.Combine(repository.WorkspacePath, "plain.scout");
        await new ScoutCompiler([new TypeScriptLanguageAnalyzer(), new MarkdownLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await QueryCommand.RunAsync(["ModelPanel", "--kernel", kernel, "--json"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("lexical", answer.GetProperty("Mode").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("Notice").ValueKind);
    }

    [Fact]
    public async Task NeverShowsTheFacetsThatExistOnlyForReadAndBriefToResolveASymbol()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "QueryHidesInternalFacets");
        var kernel = Path.Combine(repository.WorkspacePath, "billing.scout");
        await new ScoutCompiler([new Scout.Analysis.CSharp.CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await QueryCommand
            .RunAsync(["Invoice", "--kernel", kernel, "--json", "--limit", "10"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exitCode);
        foreach (var hit in JsonDocument.Parse(output.ToString()).RootElement.GetProperty("Hits").EnumerateArray())
        {
            var facetNames = hit.GetProperty("Facets").EnumerateObject().Select(facet => facet.Name);
            Assert.DoesNotContain("endLine", facetNames);
            Assert.DoesNotContain("contentHash", facetNames);
            Assert.DoesNotContain("hashAlgorithm", facetNames);
        }
    }
}

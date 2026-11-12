using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Core.Compilation;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Retrieval;

public sealed class FileVocabularyTests
{
    [Fact]
    public void KeepsDistinguishingWordsAndDropsWordsEveryFileUses()
    {
        var files = new Dictionary<string, string>
        {
            ["a.cs"] = "// reconciliation of ledger entries\nclass Alpha { void Run() { return; } }",
            ["b.cs"] = "// shipping labels\nclass Beta { void Run() { return; } }",
            ["c.cs"] = "// tax rounding\nclass Gamma { void Run() { return; } }"
        };

        var vocabulary = FileVocabulary.Build(files);

        Assert.Contains("reconciliation", vocabulary["a.cs"]);
        Assert.Contains("ledger", vocabulary["a.cs"]);
        Assert.DoesNotContain("run", vocabulary["a.cs"]);
        Assert.DoesNotContain("return", vocabulary["a.cs"]);
        Assert.DoesNotContain("reconciliation", vocabulary["b.cs"]);
    }

    [Fact]
    public void IsDeterministicAndBounded()
    {
        var words = string.Join(' ', Enumerable.Range(0, 200).Select(index => $"word{index}x"));
        var files = new Dictionary<string, string> { ["a.cs"] = words, ["b.cs"] = "other content here" };

        var first = FileVocabulary.Build(files);
        var second = FileVocabulary.Build(files);

        Assert.Equal(first["a.cs"], second["a.cs"]);
        Assert.True(first["a.cs"].Length <= FileVocabulary.MaxTermsPerFile);
    }

    [Fact]
    public void SkipsFilesTooLargeToBeProse()
    {
        var files = new Dictionary<string, string>
        {
            ["big.js"] = new string('x', FileVocabulary.MaxFileCharacters + 1),
            ["small.cs"] = "class Small { }"
        };

        Assert.DoesNotContain("big.js", FileVocabulary.Build(files).Keys);
    }

    [Fact]
    public async Task AWordOnlyInACommentFindsTheFileAndIsNeverReturned()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "vocabulary-query");
        repository.WriteFile(
            "src/Billing/Application/Notes.cs",
            "namespace Billing.Application;\n// zanzibar reconciliation happens nightly\npublic sealed class Notes { public int Value => 1; }\n");
        var kernel = Path.Combine(repository.WorkspacePath, "vocabulary.scout");
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await QueryCommand
            .RunAsync(["zanzibar", "--kernel", kernel, "--json", "--kind", "file"], output, error)
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        var hits = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("Hits");
        Assert.Equal("src/Billing/Application/Notes.cs", hits[0].GetProperty("RelativePath").GetString());
        Assert.DoesNotContain("bodyTerms", output.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("zanzibar", output.ToString().Replace("\"zanzibar\"", string.Empty), StringComparison.Ordinal);
    }
}

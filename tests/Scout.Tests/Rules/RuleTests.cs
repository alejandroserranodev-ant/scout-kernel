using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Cli.Commands;
using Scout.Cli.Rules;
using Scout.Core.Compilation;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Rules;

public sealed class RuleTests : IDisposable
{
    private const string ValidRule = """
        {
          "id": "no-console-log",
          "scope": ["src/**/*.ts"],
          "modality": "must_not",
          "check": { "type": "forbidden-call", "pattern": "console\\.log\\(" },
          "quote": { "text": "Do not call console.log in shipped code.", "source": "docs/STYLE.md#logging" },
          "state": "active",
          "reviewed": "2026-11-12"
        }
        """;

    private readonly string root = Path.Combine(Path.GetTempPath(), "Scout.Tests", Guid.NewGuid().ToString("N"));

    public RuleTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string WithState(string state) => ValidRule.Replace("\"active\"", $"\"{state}\"", StringComparison.Ordinal);

    [Fact]
    public void ParsesAValidRuleAndRoundTripsItsBytes()
    {
        var rule = RuleStore.Parse(ValidRule);

        Assert.Equal("no-console-log", rule.Id);
        Assert.Equal(RuleVocabulary.MustNot, rule.Modality);
        Assert.Equal(RuleStore.Serialize(rule), RuleStore.Serialize(RuleStore.Parse(RuleStore.Serialize(rule))));
    }

    [Theory]
    [InlineData("\"state\": \"active\"", "\"state\": \"active\", \"extra\": 1", "unknown property")]
    [InlineData("\"modality\": \"must_not\"", "\"modality\": \"maybe\"", "modality")]
    [InlineData("\"reviewed\": \"2026-11-12\"", "\"reviewed\": \"yesterday\"", "reviewed")]
    [InlineData("\"pattern\": \"console\\\\.log\\\\(\"", "\"pattern\": \"(\"", "regular expression")]
    [InlineData("\"id\": \"no-console-log\"", "\"id\": \"Bad Id\"", "id")]
    [InlineData("\"text\": \"Do not call console.log in shipped code.\", ", "", "text")]
    public void RejectsAnythingOutsideTheClosedSchema(string find, string replace, string expectedFragment)
    {
        var broken = ValidRule.Replace(find, replace, StringComparison.Ordinal);

        var exception = Assert.Throws<RuleSchemaException>(() => RuleStore.Parse(broken));
        Assert.Contains(expectedFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAParameterThatBelongsToAnotherCheckType()
    {
        var broken = ValidRule.Replace("\"type\": \"forbidden-call\",", "\"type\": \"forbidden-call\", \"testPath\": \"x\",", StringComparison.Ordinal);

        Assert.Throws<RuleSchemaException>(() => RuleStore.Parse(broken));
    }

    [Theory]
    [InlineData("src/**/*.ts", "src/a.ts", true)]
    [InlineData("src/**/*.ts", "src/deep/er/a.ts", true)]
    [InlineData("src/*.ts", "src/deep/a.ts", false)]
    [InlineData("**/*.test.ts", "a/b/c.test.ts", true)]
    [InlineData("src/a?.ts", "src/ab.ts", true)]
    [InlineData("src/*.ts", "other/a.ts", false)]
    public void GlobMatchesPaths(string glob, string path, bool expected) =>
        Assert.Equal(expected, Glob.IsMatch(glob, path));

    [Fact]
    public void ForbiddenCallReportsFileAndLine()
    {
        Write("src/a.ts", "const x = 1;\nconsole.log(x);\n");
        Write("src/b.ts", "const y = 2;\n");

        var violations = RuleChecker.Check(root, [RuleStore.Parse(ValidRule)], ["src/a.ts", "src/b.ts"]);

        var violation = Assert.Single(violations);
        Assert.Equal(("src/a.ts", 2), (violation.Path, violation.Line));
    }

    [Fact]
    public void OnlyActiveRulesAreEnforced()
    {
        Write("src/a.ts", "console.log(1);\n");

        Assert.Empty(RuleChecker.Check(root, [RuleStore.Parse(WithState("proposed"))], ["src/a.ts"]));
        Assert.Empty(RuleChecker.Check(root, [RuleStore.Parse(WithState("retired"))], ["src/a.ts"]));
    }

    [Fact]
    public void LayeringLooksOnlyAtDependencyLines()
    {
        Write("src/ui/a.ts", "import { db } from '../data/db';\n// ../data/db mentioned in a comment\nconst s = '../data/db';\n");
        var rule = RuleStore.Parse(ValidRule
            .Replace("forbidden-call", "layering", StringComparison.Ordinal)
            .Replace("\"pattern\": \"console\\\\.log\\\\(\"", "\"forbiddenImport\": \"\\\\.\\\\./data/\"", StringComparison.Ordinal)
            .Replace("src/**/*.ts", "src/ui/**", StringComparison.Ordinal));

        var violation = Assert.Single(RuleChecker.Check(root, [rule], ["src/ui/a.ts"]));

        Assert.Equal(1, violation.Line);
    }

    [Fact]
    public void NamingChecksTheFileName()
    {
        var rule = RuleStore.Parse(ValidRule
            .Replace("forbidden-call", "naming", StringComparison.Ordinal)
            .Replace("\"pattern\": \"console\\\\.log\\\\(\"", "\"pattern\": \"^[a-z]+\\\\.ts$\"", StringComparison.Ordinal));
        Write("src/good.ts", "x");
        Write("src/BadName.ts", "x");

        var violation = Assert.Single(RuleChecker.Check(root, [rule], ["src/good.ts", "src/BadName.ts"]));

        Assert.Equal("src/BadName.ts", violation.Path);
    }

    [Fact]
    public void LinkedTestRequiresTheExpectedFileToExist()
    {
        var rule = RuleStore.Parse(ValidRule
            .Replace("forbidden-call", "linked-test", StringComparison.Ordinal)
            .Replace("\"pattern\": \"console\\\\.log\\\\(\"", "\"testPath\": \"tests/{name}.test{ext}\"", StringComparison.Ordinal));
        Write("src/a.ts", "x");
        Write("src/b.ts", "x");
        Write("tests/a.test.ts", "x");

        var violation = Assert.Single(RuleChecker.Check(root, [rule], ["src/a.ts", "src/b.ts"]));

        Assert.Equal("src/b.ts", violation.Path);
        Assert.Contains("tests/b.test.ts", violation.Message, StringComparison.Ordinal);
    }

    private async Task<(int Exit, string Out, string Err)> RuleAsync(string[] arguments, string input = "", bool interactive = true)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await RuleCommand
            .RunAsync([.. arguments, "--repository", root], output, error, new StringReader(input), interactive, TimeProvider.System)
            .ConfigureAwait(true);
        return (exit, output.ToString(), error.ToString());
    }

    private static string[] Propose() =>
    [
        "propose", "no-console-log", "--scope", "src/**/*.ts", "--modality", "must_not", "--type", "forbidden-call",
        "--value", "console\\.log\\(", "--quote", "Do not call console.log.", "--source", "docs/STYLE.md#logging"
    ];

    [Fact]
    public async Task AProposalIsNotEnforcedUntilAPersonApprovesIt()
    {
        Assert.Equal(0, (await RuleAsync(Propose()).ConfigureAwait(true)).Exit);
        Write("src/a.ts", "console.log(1);\n");

        using var checkOut = new StringWriter();
        var proposedExit = await CheckCommand.RunAsync(["--repository", root], checkOut, new StringWriter()).ConfigureAwait(true);
        Assert.Equal(ExitCodes.Success, proposedExit);
        Assert.Contains("nothing to check", checkOut.ToString(), StringComparison.Ordinal);

        var approved = await RuleAsync(["approve", "no-console-log"], input: "no-console-log\n").ConfigureAwait(true);
        Assert.Equal(0, approved.Exit);

        using var enforcedOut = new StringWriter();
        var enforcedExit = await CheckCommand.RunAsync(["--repository", root, "--json"], enforcedOut, new StringWriter()).ConfigureAwait(true);
        Assert.Equal(ExitCodes.Violations, enforcedExit);
        Assert.Equal(1, JsonDocument.Parse(enforcedOut.ToString()).RootElement.GetProperty("Blocking").GetInt32());
    }

    [Fact]
    public async Task ApprovalNeedsATerminalAndTheMatchingId()
    {
        await RuleAsync(Propose()).ConfigureAwait(true);

        var scripted = await RuleAsync(["approve", "no-console-log"], input: "no-console-log\n", interactive: false).ConfigureAwait(true);
        var wrongId = await RuleAsync(["approve", "no-console-log"], input: "yes\n").ConfigureAwait(true);

        Assert.Equal(ExitCodes.Failure, scripted.Exit);
        Assert.Contains("person at a terminal", scripted.Err, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Failure, wrongId.Exit);
        Assert.Equal(RuleVocabulary.Proposed, RuleStore.LoadAll(root)[0].State);
    }

    [Fact]
    public async Task ProposingAnInvalidOrDuplicateRuleFails()
    {
        var bad = Propose();
        bad[Array.IndexOf(bad, "must_not")] = "sometimes";

        Assert.Equal(ExitCodes.Failure, (await RuleAsync(bad).ConfigureAwait(true)).Exit);
        await RuleAsync(Propose()).ConfigureAwait(true);
        Assert.Equal(ExitCodes.Failure, (await RuleAsync(Propose()).ConfigureAwait(true)).Exit);
    }

    [Fact]
    public async Task ShouldRulesWarnWithoutFailingTheCheck()
    {
        Write(".scout/rules/no-console-log.json", WithState("active").Replace("must_not", "should", StringComparison.Ordinal));
        Write("src/a.ts", "console.log(1);\n");

        using var output = new StringWriter();
        var exit = await CheckCommand.RunAsync(["--repository", root], output, new StringWriter()).ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("1 violation", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Do not call console.log", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BriefIncludesOnlyActiveRulesWhoseScopeTouchesItsFiles()
    {
        using var repository = TemporaryRepository.CopyFixture("MinimalCSharpRepository", "brief-rules");
        repository.WriteFile(
            ".scout/rules/handlers-sealed.json",
            ValidRule
                .Replace("no-console-log", "handlers-sealed", StringComparison.Ordinal)
                .Replace("src/**/*.ts", "src/Billing/**", StringComparison.Ordinal));
        repository.WriteFile(
            ".scout/rules/elsewhere.json",
            ValidRule.Replace("no-console-log", "elsewhere", StringComparison.Ordinal).Replace("src/**/*.ts", "docs/**", StringComparison.Ordinal));
        var kernel = Path.Combine(repository.WorkspacePath, "brief-rules.scout");
        await new ScoutCompiler([new CSharpLanguageAnalyzer()], indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, kernel))
            .ConfigureAwait(true);

        using var output = new StringWriter();
        var exit = await BriefCommand
            .RunAsync(["invoice", "--kernel", kernel, "--repository", repository.RootPath, "--json"], output, new StringWriter())
            .ConfigureAwait(true);

        Assert.Equal(ExitCodes.Success, exit);
        var answer = JsonDocument.Parse(output.ToString()).RootElement;
        var ids = answer.GetProperty("Rules").EnumerateArray().Select(rule => rule.GetProperty("Id").GetString()!).ToArray();
        Assert.Equal(["handlers-sealed"], ids);
        Assert.Contains("1 active rule", answer.GetProperty("RulesStatus").GetString(), StringComparison.Ordinal);
    }
}

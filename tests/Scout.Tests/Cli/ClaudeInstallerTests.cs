using System.Text.Json.Nodes;
using Scout.Cli.Install;

namespace Scout.Tests.Cli;

public sealed class ClaudeInstallerTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "Scout.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(workspace))
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private string MakeKernel(string skillMarker = "v1")
    {
        var kernel = Path.Combine(workspace, "kernel.scout");
        foreach (var skill in new[] { "scout-agents", "scout-query" })
        {
            var directory = Path.Combine(kernel, "skills", skill);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"{skill} {skillMarker}");
        }

        return kernel;
    }

    private ClaudeInstaller.InstallOptions Options(string kernel, bool dryRun = false) =>
        new(
            Path.Combine(workspace, "project", "CLAUDE.md"),
            Path.Combine(workspace, "project", ".claude"),
            kernel,
            ClaudeInstaller.ProjectScope,
            ClaudeInstaller.DefaultHookMatcher,
            dryRun);

    private static Dictionary<string, string> Snapshot(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(directory, path), File.ReadAllText);

    [Fact]
    public void InstallingTwiceProducesTheSameBytes()
    {
        var kernel = MakeKernel();
        ClaudeInstaller.Install(Options(kernel));
        var first = Snapshot(Path.Combine(workspace, "project"));

        var second = ClaudeInstaller.Install(Options(kernel));

        Assert.Empty(second.Touched);
        Assert.True(second.AlreadyInstalled);
        Assert.Equal(first, Snapshot(Path.Combine(workspace, "project")));
    }

    [Fact]
    public void ClaudeMdSectionStaysWithinItsLineBudget()
    {
        ClaudeInstaller.Install(Options(MakeKernel()));

        var content = File.ReadAllText(Path.Combine(workspace, "project", "CLAUDE.md"));
        var begin = content.IndexOf("<!-- scout:begin -->", StringComparison.Ordinal);
        var end = content.IndexOf("<!-- scout:end -->", StringComparison.Ordinal);
        var lines = content[begin..(end + "<!-- scout:end -->".Length)].Split('\n').Length;

        Assert.InRange(lines, 1, ClaudeInstaller.MaxClaudeMdSectionLines);
    }

    [Fact]
    public void HookNeverExceedsItsOutputBudgetAndAlwaysExitsZero()
    {
        var script = ClaudeHookScript.Render("kernel");

        Assert.Contains("process.exit(0)", script, StringComparison.Ordinal);
        var written = script.Split("process.stdout.write(\"")[1].Split("\\n\");")[0];
        Assert.InRange(written.Split("\\n").Length, 1, ClaudeHookScript.MaxOutputLines);
    }

    [Fact]
    public void InstallCopiesSkillsAndWritesAHookAndManifest()
    {
        ClaudeInstaller.Install(Options(MakeKernel()));

        var claude = Path.Combine(workspace, "project", ".claude");
        Assert.True(File.Exists(Path.Combine(claude, "skills", "scout-agents", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(claude, "skills", "scout-query", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(claude, "scout-install-manifest.json")));
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(claude, "settings.json")))!;
        Assert.Equal(ClaudeInstaller.DefaultHookMatcher, (string?)settings["hooks"]!["PreToolUse"]![0]!["matcher"]);
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        var result = ClaudeInstaller.Install(Options(MakeKernel(), dryRun: true));

        Assert.NotEmpty(result.Touched);
        Assert.False(Directory.Exists(Path.Combine(workspace, "project")));
    }

    [Fact]
    public void UninstallRestoresAFreshProject()
    {
        var kernel = MakeKernel();
        ClaudeInstaller.Install(Options(kernel));

        var result = ClaudeInstaller.Uninstall(Path.Combine(workspace, "project", ".claude"), dryRun: false);

        Assert.True(result.WasInstalled);
        Assert.False(File.Exists(Path.Combine(workspace, "project", "CLAUDE.md")));
        Assert.Empty(Snapshot(Path.Combine(workspace, "project")));
    }

    [Fact]
    public void UninstallKeepsUserContentInClaudeMdAndSettings()
    {
        var project = Path.Combine(workspace, "project");
        Directory.CreateDirectory(Path.Combine(project, ".claude"));
        File.WriteAllText(Path.Combine(project, "CLAUDE.md"), "# Mine\n\nKeep this.\n");
        File.WriteAllText(
            Path.Combine(project, ".claude", "settings.json"),
            """{ "permissions": { "allow": ["Bash(ls)"] } }""");
        var kernel = MakeKernel();

        ClaudeInstaller.Install(Options(kernel));
        ClaudeInstaller.Uninstall(Path.Combine(project, ".claude"), dryRun: false);

        Assert.Equal("# Mine\n\nKeep this.\n", File.ReadAllText(Path.Combine(project, "CLAUDE.md")));
        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(project, ".claude", "settings.json")))!;
        Assert.Null(settings["hooks"]);
        Assert.NotNull(settings["permissions"]);
    }

    [Fact]
    public void ResyncRefreshesInstalledSkillsFromTheKernel()
    {
        var kernel = MakeKernel("v1");
        ClaudeInstaller.Install(Options(kernel));
        File.WriteAllText(Path.Combine(kernel, "skills", "scout-query", "SKILL.md"), "scout-query v2");

        var resynced = ClaudeInstaller.ResyncSkills(Path.Combine(workspace, "project", ".claude"), kernel);

        Assert.Single(resynced);
        Assert.Equal(
            "scout-query v2",
            File.ReadAllText(Path.Combine(workspace, "project", ".claude", "skills", "scout-query", "SKILL.md")));
    }

    [Fact]
    public void ResyncIgnoresAManifestRecordedForAnotherKernel()
    {
        ClaudeInstaller.Install(Options(MakeKernel()));

        var resynced = ClaudeInstaller.ResyncSkills(
            Path.Combine(workspace, "project", ".claude"),
            Path.Combine(workspace, "other.scout"));

        Assert.Empty(resynced);
    }
}

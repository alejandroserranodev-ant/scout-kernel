using System.Text.Json;
using System.Text.Json.Nodes;

namespace Scout.Cli.Install;

/// <summary>
/// Installs (and uninstalls) Scout's Claude Code integration: a bounded CLAUDE.md section, a non-blocking
/// PreToolUse hook, and copies of the kernel's generated skills. Pure file operations on an explicit root —
/// the CLI command resolves <c>--project</c>/<c>--user</c> into paths and calls this.
/// </summary>
internal static class ClaudeInstaller
{
    internal const int MaxClaudeMdSectionLines = 15;
    internal const string DefaultHookMatcher = "Edit|Write|MultiEdit";
    internal const string ProjectScope = "project";
    internal const string UserScope = "user";

    private const string SectionBeginMarker = "<!-- scout:begin -->";
    private const string SectionEndMarker = "<!-- scout:end -->";
    private const string ManifestFileName = "scout-install-manifest.json";
    private const string SettingsFileName = "settings.json";
    private const string HookScriptRelativePath = "scout-hooks/pre-tool-use.mjs";
    private static readonly string[] SkillNames = ["scout-agents", "scout-query"];

    internal sealed record InstallOptions(
        string ClaudeMdPath,
        string ClaudeDirectory,
        string KernelPath,
        string Scope,
        string HookMatcher,
        bool DryRun);

    internal sealed record InstallResult(IReadOnlyList<string> Touched, bool AlreadyInstalled);

    internal sealed record UninstallResult(IReadOnlyList<string> Touched, bool WasInstalled);

    internal static InstallResult Install(InstallOptions options)
    {
        var kernelPath = Path.GetFullPath(options.KernelPath);
        var manifestPath = Path.Combine(options.ClaudeDirectory, ManifestFileName);
        var alreadyInstalled = File.Exists(manifestPath);
        var touched = new List<string>();

        var existingClaudeMd = File.Exists(options.ClaudeMdPath) ? File.ReadAllText(options.ClaudeMdPath) : null;
        var section = BuildClaudeMdSection(kernelPath);
        var updatedClaudeMd = MergeSection(existingClaudeMd ?? string.Empty, section);
        if (existingClaudeMd != updatedClaudeMd)
        {
            touched.Add(options.ClaudeMdPath);
            if (!options.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.ClaudeMdPath))!);
                File.WriteAllText(options.ClaudeMdPath, updatedClaudeMd);
            }
        }

        var settingsPath = Path.Combine(options.ClaudeDirectory, SettingsFileName);
        var settingsCreated = !File.Exists(settingsPath);
        var hookScriptPath = Path.Combine(options.ClaudeDirectory, HookScriptRelativePath);
        var settingsJson = ReadJsonObject(settingsPath);
        var settingsChanged = AddHook(settingsJson, options.HookMatcher, hookScriptPath);
        if (settingsChanged)
        {
            touched.Add(settingsPath);
            if (!options.DryRun)
            {
                WriteJsonObject(settingsPath, settingsJson);
            }
        }

        var hookScriptContent = ClaudeHookScript.Render(kernelPath);
        if (!File.Exists(hookScriptPath) || File.ReadAllText(hookScriptPath) != hookScriptContent)
        {
            touched.Add(hookScriptPath);
            if (!options.DryRun)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(hookScriptPath)!);
                File.WriteAllText(hookScriptPath, hookScriptContent);
            }
        }

        var installedSkillDirectories = new List<string>();
        foreach (var skillName in SkillNames)
        {
            var source = Path.Combine(kernelPath, "skills", skillName);
            if (!Directory.Exists(source))
            {
                continue;
            }

            var destination = Path.Combine(options.ClaudeDirectory, "skills", skillName);
            installedSkillDirectories.Add(destination);
            if (CopySkillDirectory(source, destination, options.DryRun))
            {
                touched.Add(destination);
            }
        }

        var previous = alreadyInstalled ? InstallManifest.FromJson(File.ReadAllText(manifestPath)) : null;
        var manifest = new InstallManifest(
            InstallManifest.CurrentFormatVersion,
            typeof(ClaudeInstaller).Assembly.GetName().Version?.ToString(3) ?? "unknown",
            options.Scope,
            kernelPath,
            options.ClaudeMdPath,
            options.HookMatcher,
            ClaudeMdCreated: previous?.ClaudeMdCreated ?? existingClaudeMd is null,
            SettingsJsonCreated: previous?.SettingsJsonCreated ?? settingsCreated,
            installedSkillDirectories);
        var manifestJson = manifest.ToJson();
        if (!File.Exists(manifestPath) || File.ReadAllText(manifestPath) != manifestJson)
        {
            touched.Add(manifestPath);
            if (!options.DryRun)
            {
                Directory.CreateDirectory(options.ClaudeDirectory);
                File.WriteAllText(manifestPath, manifestJson);
            }
        }

        return new InstallResult(touched, alreadyInstalled);
    }

    internal static UninstallResult Uninstall(string claudeDirectory, bool dryRun)
    {
        var manifestPath = Path.Combine(claudeDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return new UninstallResult([], WasInstalled: false);
        }

        var manifest = InstallManifest.FromJson(File.ReadAllText(manifestPath));
        var touched = new List<string>();

        if (File.Exists(manifest.ClaudeMdPath))
        {
            var content = File.ReadAllText(manifest.ClaudeMdPath);
            var withoutSection = RemoveSection(content);
            touched.Add(manifest.ClaudeMdPath);
            if (!dryRun)
            {
                if (manifest.ClaudeMdCreated && withoutSection.Trim().Length == 0)
                {
                    File.Delete(manifest.ClaudeMdPath);
                }
                else
                {
                    File.WriteAllText(manifest.ClaudeMdPath, withoutSection);
                }
            }
        }

        var hookScriptPath = Path.Combine(claudeDirectory, HookScriptRelativePath);
        var settingsPath = Path.Combine(claudeDirectory, SettingsFileName);
        if (File.Exists(settingsPath))
        {
            var settingsJson = ReadJsonObject(settingsPath);
            var removed = RemoveHook(settingsJson, hookScriptPath);
            if (removed)
            {
                touched.Add(settingsPath);
                if (!dryRun)
                {
                    if (manifest.SettingsJsonCreated && settingsJson.Count == 0)
                    {
                        File.Delete(settingsPath);
                    }
                    else
                    {
                        WriteJsonObject(settingsPath, settingsJson);
                    }
                }
            }
        }

        if (File.Exists(hookScriptPath))
        {
            touched.Add(hookScriptPath);
            if (!dryRun)
            {
                File.Delete(hookScriptPath);
                var hookDirectory = Path.GetDirectoryName(hookScriptPath)!;
                if (Directory.Exists(hookDirectory) && !Directory.EnumerateFileSystemEntries(hookDirectory).Any())
                {
                    Directory.Delete(hookDirectory);
                }
            }
        }

        foreach (var skillDirectory in manifest.SkillDirectories)
        {
            if (!Directory.Exists(skillDirectory))
            {
                continue;
            }

            touched.Add(skillDirectory);
            if (!dryRun)
            {
                Directory.Delete(skillDirectory, recursive: true);
            }
        }

        touched.Add(manifestPath);
        if (!dryRun)
        {
            File.Delete(manifestPath);
        }

        return new UninstallResult(touched, WasInstalled: true);
    }

    /// <summary>
    /// Re-copies the skills a prior install recorded, from the kernel path the manifest names, overwriting
    /// whatever is already there. Called by <c>scout update</c> after a kernel refresh; a no-op when no
    /// manifest exists at <paramref name="claudeDirectory"/>, or when its manifest was recorded against a
    /// different kernel than <paramref name="expectedKernelPath"/>.
    /// </summary>
    internal static IReadOnlyList<string> ResyncSkills(string claudeDirectory, string expectedKernelPath)
    {
        var manifestPath = Path.Combine(claudeDirectory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            return [];
        }

        var manifest = InstallManifest.FromJson(File.ReadAllText(manifestPath));
        if (!string.Equals(manifest.KernelPath, Path.GetFullPath(expectedKernelPath), StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var resynced = new List<string>();
        foreach (var skillDirectory in manifest.SkillDirectories)
        {
            var skillName = Path.GetFileName(skillDirectory);
            var source = Path.Combine(manifest.KernelPath, "skills", skillName);
            if (!Directory.Exists(source))
            {
                continue;
            }

            if (CopySkillDirectory(source, skillDirectory, dryRun: false))
            {
                resynced.Add(skillDirectory);
            }
        }

        return resynced;
    }

    private static bool CopySkillDirectory(string source, string destination, bool dryRun)
    {
        var changed = false;
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, sourceFile);
            var destinationFile = Path.Combine(destination, relative);
            if (!File.Exists(destinationFile) || !File.ReadAllBytes(destinationFile).SequenceEqual(File.ReadAllBytes(sourceFile)))
            {
                changed = true;
                if (!dryRun)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                }
            }
        }

        return changed;
    }

    private static string BuildClaudeMdSection(string kernelPath)
    {
        var lines = new[]
        {
            SectionBeginMarker,
            "## Scout Kernel",
            $"A compiled Scout Kernel for this project lives at `{kernelPath}`.",
            "Prefer `scout_brief` / `scout_read` / `scout_neighbors` (MCP) or `scout query \"<task>\" --kernel " +
                $"{kernelPath}` over blind grep+read.",
            $"Kernel index: `{kernelPath}/KERNEL.md`.",
            SectionEndMarker
        };

        if (lines.Length > MaxClaudeMdSectionLines)
        {
            throw new InvalidOperationException("The generated CLAUDE.md section exceeds its line budget.");
        }

        return string.Join("\n", lines);
    }

    private static string MergeSection(string existingContent, string section)
    {
        var (beforeBegin, afterEnd) = SplitOnSection(existingContent);
        if (beforeBegin is null)
        {
            var trimmed = existingContent.TrimEnd('\n', '\r');
            return trimmed.Length == 0 ? section + "\n" : trimmed + "\n\n" + section + "\n";
        }

        return beforeBegin + section + afterEnd;
    }

    private static string RemoveSection(string existingContent)
    {
        var (beforeBegin, afterEnd) = SplitOnSection(existingContent);
        if (beforeBegin is null)
        {
            return existingContent;
        }

        var merged = beforeBegin.TrimEnd('\n', '\r') + afterEnd.TrimStart('\n', '\r');
        var trimmed = merged.TrimEnd('\n', '\r');
        return trimmed.Trim().Length == 0 ? string.Empty : trimmed + "\n";
    }

    // Finds the exact byte span of a previously installed section (markers included) so it can be replaced or
    // removed without disturbing anything a user wrote before or after it. Returns (null, content) when no
    // section is present yet.
    private static (string? BeforeBegin, string AfterEnd) SplitOnSection(string content)
    {
        var beginIndex = content.IndexOf(SectionBeginMarker, StringComparison.Ordinal);
        if (beginIndex < 0)
        {
            return (null, content);
        }

        var endIndex = content.IndexOf(SectionEndMarker, beginIndex, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return (null, content);
        }

        var before = content[..beginIndex].TrimEnd('\n', '\r');
        before = before.Length == 0 ? string.Empty : before + "\n\n";
        var after = content[(endIndex + SectionEndMarker.Length)..];
        return (before, after);
    }

    private static JsonObject ReadJsonObject(string path) =>
        File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject()
            : new JsonObject();

    private static void WriteJsonObject(string path, JsonObject json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // The hook is identified by its own generated command string, so adding it twice (re-running install) or
    // removing it later never touches a matcher entry or command the user added for anything else.
    private static bool AddHook(JsonObject settings, string matcher, string hookScriptPath)
    {
        var command = BuildHookCommand(hookScriptPath);
        var hooks = (JsonObject)(settings["hooks"] ??= new JsonObject());
        var preToolUse = (JsonArray)(hooks["PreToolUse"] ??= new JsonArray());

        foreach (var entry in preToolUse.OfType<JsonObject>())
        {
            if (entry["hooks"] is JsonArray existingHooks &&
                existingHooks.OfType<JsonObject>().Any(h => h["command"]?.GetValue<string>() == command))
            {
                return false;
            }
        }

        preToolUse.Add(new JsonObject
        {
            ["matcher"] = matcher,
            ["hooks"] = new JsonArray { new JsonObject { ["type"] = "command", ["command"] = command } }
        });
        return true;
    }

    private static bool RemoveHook(JsonObject settings, string hookScriptPath)
    {
        var command = BuildHookCommand(hookScriptPath);
        if (settings["hooks"] is not JsonObject hooks || hooks["PreToolUse"] is not JsonArray preToolUse)
        {
            return false;
        }

        var removedAny = false;
        for (var index = preToolUse.Count - 1; index >= 0; index--)
        {
            if (preToolUse[index] is not JsonObject entry || entry["hooks"] is not JsonArray entryHooks)
            {
                continue;
            }

            for (var hookIndex = entryHooks.Count - 1; hookIndex >= 0; hookIndex--)
            {
                if (entryHooks[hookIndex] is JsonObject h && h["command"]?.GetValue<string>() == command)
                {
                    entryHooks.RemoveAt(hookIndex);
                    removedAny = true;
                }
            }

            if (entryHooks.Count == 0)
            {
                preToolUse.RemoveAt(index);
            }
        }

        if (preToolUse.Count == 0)
        {
            hooks.Remove("PreToolUse");
        }

        if (hooks.Count == 0)
        {
            settings.Remove("hooks");
        }

        return removedAny;
    }

    private static string BuildHookCommand(string hookScriptPath) => $"node \"{hookScriptPath}\"";
}

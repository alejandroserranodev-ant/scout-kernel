using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scout.Cli.Install;

/// <summary>
/// Records exactly what <c>scout install claude</c> wrote, so <c>scout update</c> can resync the skills it
/// copied and <c>--uninstall</c> can remove only what Scout itself added, never a user's own content.
/// </summary>
internal sealed record InstallManifest(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("toolVersion")] string ToolVersion,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("kernelPath")] string KernelPath,
    [property: JsonPropertyName("claudeMdPath")] string ClaudeMdPath,
    [property: JsonPropertyName("hookMatcher")] string HookMatcher,
    [property: JsonPropertyName("claudeMdCreated")] bool ClaudeMdCreated,
    [property: JsonPropertyName("settingsJsonCreated")] bool SettingsJsonCreated,
    [property: JsonPropertyName("skillDirectories")] IReadOnlyList<string> SkillDirectories)
{
    internal const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    internal static InstallManifest FromJson(string json) =>
        JsonSerializer.Deserialize<InstallManifest>(json)
        ?? throw new InvalidOperationException("The install manifest is empty.");
}

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Scout.Analysis.TypeScript;

/// <summary>
/// Runs the Node sidecar that reads TypeScript sources through the official compiler API.
/// </summary>
/// <remarks>
/// Scout keeps the TypeScript compiler out of process on purpose. Reimplementing TypeScript resolution in C#
/// would produce plausible but unverified relationships, and an unverified relationship is worse than a missing
/// one because the rest of the pipeline treats relationships as observed facts.
/// </remarks>
internal static class TypeScriptSidecar
{
    private const string ScriptFileName = "scout-ts-sidecar.mjs";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Locates the sidecar entry script published beside the analyzer assembly.
    /// </summary>
    /// <returns>The absolute script path, or <see langword="null"/> when the sidecar was not published.</returns>
    internal static string? FindScriptPath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "sidecar", ScriptFileName);
        return File.Exists(candidate) ? candidate : null;
    }

    internal static async Task<SidecarResponse> RunAsync(
        string scriptPath,
        string rootPath,
        IReadOnlyList<string> relativeFiles,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            WorkingDirectory = rootPath
        };
        startInfo.ArgumentList.Add(scriptPath);

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Node could not be started.");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return CreateFailure(
                "TS_NODE_UNAVAILABLE",
                $"Node.js is required for TypeScript analysis but could not be started: {exception.Message}");
        }

        using (process)
        {
            var request = new SidecarRequest { RootPath = rootPath, Files = relativeFiles };
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await using (var input = process.StandardInput)
            {
                await input.WriteAsync(JsonSerializer.Serialize(request, SerializerOptions)).ConfigureAwait(false);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(standardOutput))
            {
                var reason = string.IsNullOrWhiteSpace(standardError)
                    ? $"the sidecar exited with code {process.ExitCode} without output"
                    : standardError.Trim();
                return CreateFailure("TS_SIDECAR_NO_OUTPUT", $"TypeScript analysis produced no payload: {reason}");
            }

            try
            {
                return JsonSerializer.Deserialize<SidecarResponse>(standardOutput, SerializerOptions)
                    ?? CreateFailure("TS_SIDECAR_EMPTY_PAYLOAD", "TypeScript analysis returned an empty payload.");
            }
            catch (JsonException exception)
            {
                return CreateFailure(
                    "TS_SIDECAR_INVALID_PAYLOAD",
                    $"TypeScript analysis returned an unreadable payload: {exception.Message}");
            }
        }
    }

    private static SidecarResponse CreateFailure(string code, string message)
    {
        return new SidecarResponse
        {
            Status = "failed",
            Diagnostics = [new SidecarDiagnostic { Severity = "error", Code = code, Message = message }]
        };
    }
}

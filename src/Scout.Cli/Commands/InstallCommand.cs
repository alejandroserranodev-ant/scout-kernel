using Scout.Cli.Install;

namespace Scout.Cli.Commands;

/// <summary>
/// <c>scout install claude</c>: wires a compiled kernel into Claude Code — a bounded CLAUDE.md section, a
/// non-blocking PreToolUse hook, and copies of the kernel's generated skills. Idempotent: running it again
/// with the same arguments touches nothing further. <c>--uninstall</c> removes only what a prior install
/// recorded in its manifest.
/// </summary>
internal static class InstallCommand
{
    internal const string Verb = "install";

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        if (arguments.Count == 0 || arguments[0] != "claude")
        {
            return await Usage(error, "The install command currently supports only 'claude': scout install claude.")
                .ConfigureAwait(false);
        }

        string? kernelPath = null;
        string? root = null;
        var scope = ClaudeInstaller.ProjectScope;
        var hookMatcher = ClaudeInstaller.DefaultHookMatcher;
        var dryRun = false;
        var uninstall = false;

        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--kernel":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --kernel option requires a path.").ConfigureAwait(false);
                    }

                    kernelPath = arguments[index];
                    break;

                case "--root":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --root option requires a path.").ConfigureAwait(false);
                    }

                    root = arguments[index];
                    break;

                case "--project":
                    scope = ClaudeInstaller.ProjectScope;
                    break;

                case "--user":
                    scope = ClaudeInstaller.UserScope;
                    break;

                case "--hook-matcher":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --hook-matcher option requires a value.").ConfigureAwait(false);
                    }

                    hookMatcher = arguments[index];
                    break;

                case "--dry-run":
                    dryRun = true;
                    break;

                case "--uninstall":
                    uninstall = true;
                    break;

                default:
                    return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
            }
        }

        root ??= scope == ClaudeInstaller.UserScope
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Directory.GetCurrentDirectory();
        root = Path.GetFullPath(root);
        var claudeDirectory = Path.Combine(root, ".claude");
        var claudeMdPath = scope == ClaudeInstaller.UserScope
            ? Path.Combine(claudeDirectory, "CLAUDE.md")
            : Path.Combine(root, "CLAUDE.md");

        if (uninstall)
        {
            var uninstallResult = ClaudeInstaller.Uninstall(claudeDirectory, dryRun);
            if (!uninstallResult.WasInstalled)
            {
                await output.WriteLineAsync($"Nothing to uninstall: no Scout install manifest under '{claudeDirectory}'.")
                    .ConfigureAwait(false);
                return ExitCodes.Success;
            }

            await WriteTouched(output, dryRun, "Removed", uninstallResult.Touched).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        if (string.IsNullOrWhiteSpace(kernelPath))
        {
            return await Usage(error, "The install command requires --kernel <path>.").ConfigureAwait(false);
        }

        if (!Directory.Exists(kernelPath))
        {
            await error.WriteLineAsync($"error: no kernel directory at '{kernelPath}'.").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var installResult = ClaudeInstaller.Install(new ClaudeInstaller.InstallOptions(
            claudeMdPath, claudeDirectory, kernelPath, scope, hookMatcher, dryRun));

        if (installResult.Touched.Count == 0)
        {
            await output.WriteLineAsync("Already installed; nothing changed.").ConfigureAwait(false);
            return ExitCodes.Success;
        }

        await WriteTouched(output, dryRun, "Wrote", installResult.Touched).ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private static async Task WriteTouched(TextWriter output, bool dryRun, string verb, IReadOnlyList<string> paths)
    {
        var prefix = dryRun ? $"Would have {verb.ToLowerInvariant()}" : verb;
        foreach (var path in paths)
        {
            await output.WriteLineAsync($"{prefix}: {path}").ConfigureAwait(false);
        }
    }

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout install claude --kernel <path> [--project|--user] [--root <path>] " +
            "[--hook-matcher <pattern>] [--dry-run] [--uninstall]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

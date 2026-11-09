using Microsoft.Build.Locator;

namespace Scout.Analysis.CSharp;

internal static class MsBuildRegistration
{
    private static readonly object Gate = new();
    private static string? registeredPath;

    public static MsBuildRegistrationResult EnsureRegistered(string repositoryRoot)
    {
        var rootPath = Path.GetFullPath(repositoryRoot);
        var options = VisualStudioInstanceQueryOptions.Default;
        options.WorkingDirectory = rootPath;

        var selectedInstance = MSBuildLocator.QueryVisualStudioInstances(options).FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No compatible .NET SDK could be resolved for repository '{rootPath}'.");

        lock (Gate)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterInstance(selectedInstance);
                registeredPath = selectedInstance.MSBuildPath;
                return new MsBuildRegistrationResult(selectedInstance.Version.ToString(), null);
            }

            if (registeredPath is null)
            {
                return new MsBuildRegistrationResult(
                    selectedInstance.Version.ToString(),
                    "MSBuild was registered by another component; Scout could not verify that it matches the repository SDK.");
            }

            if (!PathsEqual(registeredPath, selectedInstance.MSBuildPath))
            {
                throw new InvalidOperationException(
                    $"MSBuild is already registered from '{registeredPath}', but repository '{rootPath}' resolves " +
                    $"SDK '{selectedInstance.MSBuildPath}'. Analyze repositories requiring different SDKs in separate processes.");
            }

            return new MsBuildRegistrationResult(selectedInstance.Version.ToString(), null);
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }
}

internal readonly record struct MsBuildRegistrationResult(string Version, string? Warning);

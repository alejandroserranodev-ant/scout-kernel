using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Scout.Tests.Support;

internal sealed class TemporaryRepository : IDisposable
{
    private readonly string workspacePath;
    private bool disposed;

    private TemporaryRepository(string workspacePath, string rootPath)
    {
        this.workspacePath = workspacePath;
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public string WorkspacePath => workspacePath;

    public static TemporaryRepository CopyFixture(
        string fixtureName,
        string directoryName,
        bool reverseFileCreationOrder = false)
    {
        ValidateSinglePathSegment(fixtureName, nameof(fixtureName));
        ValidateSinglePathSegment(directoryName, nameof(directoryName));

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        if (!Directory.Exists(fixturePath))
        {
            throw new DirectoryNotFoundException($"Test fixture does not exist: {fixturePath}");
        }

        var workspacePath = Path.Combine(
            Path.GetTempPath(),
            "Scout.Tests",
            Guid.NewGuid().ToString("N"));
        var rootPath = Path.Combine(workspacePath, directoryName);
        Directory.CreateDirectory(rootPath);

        var fixtureFiles = Directory
            .EnumerateFiles(fixturePath, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (reverseFileCreationOrder)
        {
            Array.Reverse(fixtureFiles);
        }

        foreach (var sourcePath in fixtureFiles)
        {
            var relativePath = Path.GetRelativePath(fixturePath, sourcePath);
            var destinationPath = Path.Combine(rootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath);
        }

        return new TemporaryRepository(workspacePath, rootPath);
    }

    public void WriteFile(string relativePath, string content)
    {
        WriteFileBelow(RootPath, relativePath, content);
    }

    public async Task InitializeWorkspaceGitRepositoryAsync()
    {
        await RunGitAsync(["init", "--quiet"]).ConfigureAwait(false);
        await RunGitAsync(
            [
                "-c", "user.name=Scout Tests",
                "-c", "user.email=scout-tests@example.invalid",
                "commit", "--quiet", "--allow-empty", "-m", "Initial fixture revision"
            ]).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (Directory.Exists(workspacePath))
        {
            foreach (var filePath in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(workspacePath, recursive: true);
        }
    }

    private static void WriteFileBelow(string rootPath, string relativePath, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var fullRootPath = Path.GetFullPath(rootPath);
        var destinationPath = Path.GetFullPath(Path.Combine(
            fullRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relativeDestinationPath = Path.GetRelativePath(fullRootPath, destinationPath);
        if (relativeDestinationPath == ".." ||
            relativeDestinationPath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativeDestinationPath))
        {
            throw new ArgumentException("The file must remain below the temporary repository root.", nameof(relativePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private async Task RunGitAsync(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workspacePath
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(workspacePath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Git could not be started for the test fixture.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Git is required for the parent-repository test.", exception);
        }

        using (process)
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
        {
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardErrorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Git failed for the test fixture with exit code {process.ExitCode}: " +
                    $"{standardError}{standardOutput}");
            }
        }
    }

    private static void ValidateSinglePathSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." ||
            value.Contains('/') ||
            value.Contains('\\') ||
            Path.IsPathRooted(value) ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The value must be a portable single path segment.", parameterName);
        }
    }
}

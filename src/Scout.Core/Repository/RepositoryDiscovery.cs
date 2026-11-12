using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Scout.Core.Repository;

/// <summary>
/// Discovers and fingerprints analyzer inputs without reading excluded build or tool directories.
/// </summary>
public sealed class RepositoryDiscovery
{
    private static readonly string[] DefaultExtensions = [".sln", ".slnx", ".csproj", ".cs"];
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".claude",
        ".git",
        ".hg",
        ".svn",
        ".idea",
        ".vs",
        "bin",
        "node_modules",
        "obj"
    };

    private readonly HashSet<string> includedExtensions;

    /// <summary>
    /// Initializes a discovery instance for the supplied input extensions.
    /// </summary>
    /// <param name="includedExtensions">
    /// Extensions contributed by active analyzers. The first-slice C# extensions are used when omitted.
    /// </param>
    public RepositoryDiscovery(IEnumerable<string>? includedExtensions = null)
    {
        this.includedExtensions = new HashSet<string>(
            (includedExtensions ?? DefaultExtensions).Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        if (this.includedExtensions.Count == 0)
        {
            throw new ArgumentException("At least one input extension is required.", nameof(includedExtensions));
        }
    }

    /// <summary>
    /// Discovers and fingerprints relevant files below a repository root.
    /// </summary>
    /// <param name="repositoryPath">The repository root.</param>
    /// <param name="cancellationToken">A token that cancels discovery.</param>
    /// <returns>The deterministic repository descriptor.</returns>
    public async Task<RepositoryDescriptor> DiscoverAsync(
        string repositoryPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        var rootPath = Path.GetFullPath(repositoryPath);
        if (!Directory.Exists(rootPath))
        {
            throw new DirectoryNotFoundException($"Repository directory does not exist: {rootPath}");
        }

        var diagnostics = new List<string>();
        var paths = EnumerateInputFiles(rootPath, diagnostics, cancellationToken)
            .Order(StringComparer.Ordinal)
            .ToArray();
        paths = await RemoveGitIgnoredAsync(rootPath, paths, diagnostics, cancellationToken).ConfigureAwait(false);

        var files = new List<DiscoveredFile>(paths.Length);
        foreach (var relativePath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var absolutePath = RepositoryPath.Resolve(rootPath, relativePath);
            var fileInfo = new FileInfo(absolutePath);
            var contentHash = await ComputeFileHashAsync(absolutePath, cancellationToken).ConfigureAwait(false);
            files.Add(new DiscoveredFile(relativePath, fileInfo.Length, contentHash));
        }

        var fingerprint = ComputeRepositoryFingerprint(files);
        var revision = await ReadGitRevisionAsync(rootPath, diagnostics, cancellationToken).ConfigureAwait(false);
        var name = new DirectoryInfo(rootPath).Name;

        return new RepositoryDescriptor(rootPath, name, files, fingerprint, revision, diagnostics);
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        return normalized.ToLowerInvariant();
    }

    private IEnumerable<string> EnumerateInputFiles(
        string rootPath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] childDirectories;
            string[] files;
            try
            {
                childDirectories = Directory.GetDirectories(directory);
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Skipped '{RepositoryPath.GetRelativePath(rootPath, directory)}': {exception.Message}");
                continue;
            }

            foreach (var file in files.Order(StringComparer.Ordinal))
            {
                if (includedExtensions.Contains(Path.GetExtension(file)))
                {
                    yield return RepositoryPath.GetRelativePath(rootPath, file);
                }
            }

            foreach (var childDirectory in childDirectories.OrderDescending(StringComparer.Ordinal))
            {
                var info = new DirectoryInfo(childDirectory);
                if (ShouldExclude(info))
                {
                    continue;
                }

                pending.Push(childDirectory);
            }
        }
    }

    private static bool ShouldExclude(DirectoryInfo directory)
    {
        return ExcludedDirectoryNames.Contains(directory.Name) ||
               directory.Name.EndsWith(".scout", StringComparison.OrdinalIgnoreCase) ||
               directory.Name.StartsWith(".next", StringComparison.OrdinalIgnoreCase) ||
               directory.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    private static async Task<string> ComputeFileHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static string ComputeRepositoryFingerprint(IEnumerable<DiscoveredFile> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            hash.AppendData([0]);
            hash.AppendData(Convert.FromHexString(file.ContentHash));
            hash.AppendData([0]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static async Task<SourceRevision> ReadGitRevisionAsync(
        string rootPath,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            var revisionResult = await RunGitAsync(
                rootPath,
                ["rev-parse", "--show-toplevel", "HEAD"],
                cancellationToken).ConfigureAwait(false);

            if (revisionResult.ExitCode != 0)
            {
                return SourceRevision.Unknown;
            }

            var lines = revisionResult.StandardOutput
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length < 2 || !PathsEqual(rootPath, lines[0]))
            {
                return SourceRevision.Unknown;
            }

            var statusResult = await RunGitAsync(
                rootPath,
                ["status", "--porcelain", "--untracked-files=no"],
                cancellationToken).ConfigureAwait(false);
            var isDirty = statusResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(statusResult.StandardOutput);

            return new SourceRevision("git-commit", lines[1], isDirty);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            diagnostics.Add($"Git revision was unavailable: {exception.Message}");
            return SourceRevision.Unknown;
        }
    }

    // Build output and caches a project's own .gitignore already excludes (dist, coverage, generated bundles) are not
    // part of its source: indexing them buries real declarations under minified code. Outside a git repository, or
    // if git cannot answer, nothing is filtered.
    private static async Task<string[]> RemoveGitIgnoredAsync(
        string rootPath,
        string[] paths,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (paths.Length == 0)
        {
            return paths;
        }

        try
        {
            var result = await RunGitAsync(
                rootPath,
                ["ls-files", "--others", "--ignored", "--exclude-standard", "--directory", "-z"],
                cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return paths;
            }

            var ignored = result.StandardOutput.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (ignored.Length == 0)
            {
                return paths;
            }

            var ignoredDirectories = ignored.Where(entry => entry.EndsWith('/')).ToArray();
            var ignoredFiles = ignored.Where(entry => !entry.EndsWith('/')).ToHashSet(StringComparer.Ordinal);
            var kept = paths
                .Where(path => !ignoredFiles.Contains(path)
                    && !ignoredDirectories.Any(directory => path.StartsWith(directory, StringComparison.Ordinal)))
                .ToArray();
            if (kept.Length != paths.Length)
            {
                diagnostics.Add($"Skipped {paths.Length - kept.Length} file(s) ignored by the repository's .gitignore.");
            }

            return kept;
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return paths;
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

    private static async Task<ProcessResult> RunGitAsync(
        string rootPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = rootPath
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(rootPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(
            process.ExitCode,
            await standardOutputTask.ConfigureAwait(false),
            await standardErrorTask.ConfigureAwait(false));
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

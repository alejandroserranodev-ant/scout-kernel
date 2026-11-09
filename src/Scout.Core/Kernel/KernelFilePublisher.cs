using System.Text.Json;

namespace Scout.Core.Kernel;

internal static class KernelFilePublisher
{
    internal static async Task PublishAsync(
        string outputPath,
        IReadOnlyList<KernelArtifact> artifacts,
        CancellationToken cancellationToken)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutputPath))
        {
            throw new IOException($"Kernel output path is an existing file: {fullOutputPath}");
        }

        var parentPath = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("Kernel output must have a parent directory.");
        Directory.CreateDirectory(parentPath);
        var stagingPath = Path.Combine(parentPath, $".scout-staging-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(parentPath, $".scout-backup-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingPath);
            foreach (var artifact in artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var targetPath = ResolveBelow(stagingPath, artifact.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await File.WriteAllBytesAsync(targetPath, artifact.Content, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!Directory.Exists(fullOutputPath))
            {
                Directory.Move(stagingPath, fullOutputPath);
                return;
            }

            EnsureExistingScoutKernel(fullOutputPath);
            Directory.Move(fullOutputPath, backupPath);
            try
            {
                Directory.Move(stagingPath, fullOutputPath);
            }
            catch
            {
                Directory.Move(backupPath, fullOutputPath);
                throw;
            }

            Directory.Delete(backupPath, recursive: true);
        }
        finally
        {
            if (Directory.Exists(stagingPath))
            {
                Directory.Delete(stagingPath, recursive: true);
            }
        }
    }

    private static string ResolveBelow(string rootPath, string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized[0] == '/' || normalized.Contains("../", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Kernel artifact path is not safe: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(fullRoot, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Kernel artifact path escapes its root: {relativePath}");
        }

        return candidate;
    }

    private static void EnsureExistingScoutKernel(string outputPath)
    {
        var manifestPath = Path.Combine(outputPath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new IOException($"Refusing to replace a directory that is not a Scout Kernel: {outputPath}");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("format", out var format) ||
            format.GetString() != "scout-kernel")
        {
            throw new IOException($"Refusing to replace a directory that is not a Scout Kernel: {outputPath}");
        }
    }
}

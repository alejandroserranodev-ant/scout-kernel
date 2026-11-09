using Microsoft.CodeAnalysis;
using Scout.Core.Ir;
using Scout.Core.Repository;
using RepositoryPaths = Scout.Core.Repository.RepositoryPath;

namespace Scout.Analysis.CSharp;

internal sealed class RepositoryFileIndex
{
    private readonly string repositoryRoot;
    private readonly Dictionary<string, DiscoveredFile> sourceFiles;

    public RepositoryFileIndex(RepositoryDescriptor repository)
    {
        repositoryRoot = Path.GetFullPath(repository.RootPath);
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        sourceFiles = repository.Files
            .Where(static file => string.Equals(Path.GetExtension(file.RelativePath), ".cs", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static file => file.RelativePath, pathComparer);
    }

    public bool TryGetFile(string? path, string? baseDirectory, out IndexedSourceFile sourceFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            sourceFile = default;
            return false;
        }

        try
        {
            var absolutePath = Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(path, baseDirectory ?? repositoryRoot);
            var relativePath = RepositoryPaths.GetRelativePath(repositoryRoot, absolutePath);
            if (!sourceFiles.TryGetValue(relativePath, out var discoveredFile))
            {
                sourceFile = default;
                return false;
            }

            var id = StructuralId.Create("source-file", discoveredFile.RelativePath);
            sourceFile = new IndexedSourceFile(
                id,
                discoveredFile.RelativePath,
                discoveredFile.ContentHash);
            return true;
        }
        catch (ArgumentException)
        {
            sourceFile = default;
            return false;
        }
    }

    public SourceLocation? TryCreateLocation(Location location, string? baseDirectory = null)
    {
        if (!location.IsInSource || location.SourceTree is null ||
            !TryGetFile(location.SourceTree.FilePath, baseDirectory, out var sourceFile))
        {
            return null;
        }

        var span = location.GetLineSpan().Span;
        return new SourceLocation(
            sourceFile.RelativePath,
            span.Start.Line + 1,
            span.Start.Character + 1,
            span.End.Line + 1,
            span.End.Character + 1,
            "utf-16-code-units",
            sourceFile.Id);
    }
}

internal readonly record struct IndexedSourceFile(
    StructuralId Id,
    string RelativePath,
    string ContentHash);

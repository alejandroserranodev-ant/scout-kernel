using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;

namespace Scout.Cli.Commands;

/// <summary>
/// Returns the exact source slice a symbol or a file range names, read from the live repository — never from an
/// indexed copy, since a kernel never stores source bodies.
/// </summary>
/// <remarks>
/// This is the command <c>scout brief</c> expects an agent to follow up with: retrieval names where to look,
/// this opens exactly that and nothing around it.
/// </remarks>
internal static partial class ReadCommand
{
    internal const string Verb = "read";

    private const string IndexRelativePath = "retrieval/index.db";
    private const string KernelDirectorySuffix = ".scout";
    private const int DefaultMaxLines = 400;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        string? target = null;
        string? kernelPath = null;
        string? repositoryPath = null;
        var maxLines = DefaultMaxLines;
        var asJson = false;
        var compact = false;

        for (var index = 0; index < arguments.Count; index++)
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

                case "--repository":
                    if (++index >= arguments.Count)
                    {
                        return await Usage(error, "The --repository option requires a path.").ConfigureAwait(false);
                    }

                    repositoryPath = arguments[index];
                    break;

                case "--max-lines":
                    if (++index >= arguments.Count
                        || !int.TryParse(arguments[index], CultureInfo.InvariantCulture, out maxLines)
                        || maxLines <= 0)
                    {
                        return await Usage(error, "The --max-lines option requires a positive integer.").ConfigureAwait(false);
                    }

                    break;

                case "--json":
                    asJson = true;
                    break;

                case "--format":
                    if (++index >= arguments.Count || arguments[index] != "compact")
                    {
                        return await Usage(error, "The --format option only accepts 'compact'.").ConfigureAwait(false);
                    }

                    compact = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return await Usage(error, $"Unknown option '{argument}'.").ConfigureAwait(false);
                    }

                    if (target is not null)
                    {
                        return await Usage(error, "Only one symbol or file:lines target can be read at a time.").ConfigureAwait(false);
                    }

                    target = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return await Usage(error, "The read command requires a symbol name or a file:lines target.").ConfigureAwait(false);
        }

        if (kernelPath is null)
        {
            return await Usage(error, "The read command requires --kernel <path>.").ConfigureAwait(false);
        }

        var repositoryRoot = repositoryPath ?? InferRepositoryRoot(kernelPath);
        if (repositoryRoot is null)
        {
            await error.WriteLineAsync(
                $"error: cannot infer the source repository from '{kernelPath}' (it does not end in '{KernelDirectorySuffix}'). Pass --repository <path>.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var fileRangeMatch = FileRangePattern().Match(target);
        ResolvedTarget resolved;
        if (fileRangeMatch.Success)
        {
            resolved = ResolveFileRange(fileRangeMatch);
        }
        else
        {
            var indexPath = ResolveIndexPath(kernelPath);
            if (!File.Exists(indexPath))
            {
                await error.WriteLineAsync($"error: no retrieval index at '{indexPath}'. Compile without --no-index first.")
                    .ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            using var database = SqliteRetrievalDatabase.Open(indexPath, new LexicalEmbeddingModel());
            // A symbol with enough consumers is promoted to a richer "contract" card instead of a "symbol" one
            // (see RetrievalCardCompiler); the two are mutually exclusive per name, so trying both in order finds
            // either without the caller having to know which kind a given name landed in.
            var matches = database.FindByExactName(KnownCardKinds.Symbol, target);
            if (matches.Count == 0)
            {
                matches = database.FindByExactName(KnownCardKinds.Contract, target);
            }

            if (matches.Count == 0)
            {
                await error.WriteLineAsync(
                    $"error: no symbol named '{target}' in this kernel. Try `scout query \"{target}\" --kernel \"{kernelPath}\"` " +
                    "to find the closest match, or use a file:start-end target directly.")
                    .ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            if (matches.Count > 1)
            {
                await error.WriteLineAsync($"error: '{target}' names {matches.Count} symbols; read one directly by file:start-end instead:")
                    .ConfigureAwait(false);
                foreach (var match in matches)
                {
                    await error.WriteLineAsync($"  {match.RelativePath}:{FacetValue(match, KnownFacetNames.Line) ?? "?"}")
                        .ConfigureAwait(false);
                }

                return ExitCodes.Failure;
            }

            var resolution = ResolveSymbol(matches[0]);
            if (resolution is null)
            {
                await error.WriteLineAsync($"error: symbol '{target}' has no recorded source location in this kernel.")
                    .ConfigureAwait(false);
                return ExitCodes.Failure;
            }

            resolved = resolution;
        }

        var absolutePath = Path.Combine(repositoryRoot, resolved.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(absolutePath))
        {
            await error.WriteLineAsync($"error: '{resolved.RelativePath}' does not exist under repository root '{repositoryRoot}'.")
                .ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        var allLines = await File.ReadAllLinesAsync(absolutePath, cancellationToken).ConfigureAwait(false);
        var startLine = Math.Clamp(resolved.StartLine, 1, allLines.Length);
        var endLine = Math.Clamp(resolved.EndLine, startLine, allLines.Length);
        var truncated = endLine - startLine + 1 > maxLines;
        if (truncated)
        {
            endLine = startLine + maxLines - 1;
        }

        var slice = allLines[(startLine - 1)..endLine];
        var stale = IsStale(resolved, absolutePath);

        await output.WriteLineAsync(
            asJson
                ? RenderJson(resolved, startLine, endLine, slice, truncated, stale)
                : RenderText(resolved, startLine, endLine, slice, truncated, stale, compact))
            .ConfigureAwait(false);
        return ExitCodes.Success;
    }

    private sealed record ResolvedTarget(
        string RelativePath, int StartLine, int EndLine, string? ContentHash, string? HashAlgorithm, bool MaybeHeaderOnly);

    private static ResolvedTarget ResolveFileRange(Match match)
    {
        var relativePath = match.Groups["path"].Value;
        var start = int.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
        var end = match.Groups["end"].Success ? int.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture) : start;
        return new ResolvedTarget(relativePath, start, end, ContentHash: null, HashAlgorithm: null, MaybeHeaderOnly: false);
    }

    private static ResolvedTarget? ResolveSymbol(RetrievalCard card)
    {
        var startText = FacetValue(card, KnownFacetNames.Line);
        if (startText is null || !int.TryParse(startText, CultureInfo.InvariantCulture, out var start))
        {
            return null;
        }

        var endText = FacetValue(card, KnownFacetNames.EndLine);
        var end = endText is not null && int.TryParse(endText, CultureInfo.InvariantCulture, out var parsedEnd) ? parsedEnd : start;
        return new ResolvedTarget(
            card.RelativePath,
            start,
            end,
            FacetValue(card, KnownFacetNames.ContentHash),
            FacetValue(card, KnownFacetNames.HashAlgorithm),
            // A class/interface's recorded span covers its whole body, but a member's recorded location is
            // currently its name token only (a Roslyn ISymbol.Locations limitation, not a deliberate choice) —
            // a single-line span is the tell. Said plainly here instead of silently returning a signature line
            // as if it were the body.
            MaybeHeaderOnly: start == end);
    }

    private static string? FacetValue(RetrievalCard card, string name) =>
        card.Facets.FirstOrDefault(facet => facet.Name == name).Value is { Length: > 0 } value ? value : null;

    private static bool IsStale(ResolvedTarget resolved, string absolutePath)
    {
        if (resolved.ContentHash is null || !string.Equals(resolved.HashAlgorithm, "sha256", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var liveHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(absolutePath)));
        return !string.Equals(liveHash, resolved.ContentHash, StringComparison.OrdinalIgnoreCase);
    }

    // A kernel's default output directory is a sibling of its source named "<repository>.scout" (see `compile`),
    // so the source is recoverable from the kernel path without the kernel ever having to record an absolute,
    // machine-specific path inside itself.
    private static string? InferRepositoryRoot(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.EndsWith(KernelDirectorySuffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full[..^KernelDirectorySuffix.Length];
    }

    private static string ResolveIndexPath(string kernelPath)
    {
        var full = Path.GetFullPath(kernelPath);
        return Directory.Exists(full) ? Path.Combine(full, IndexRelativePath) : full;
    }

    private static string RenderJson(
        ResolvedTarget resolved, int startLine, int endLine, string[] lines, bool truncated, bool stale) =>
        JsonSerializer.Serialize(
            new
            {
                resolved.RelativePath,
                StartLine = startLine,
                EndLine = endLine,
                Truncated = truncated,
                Stale = stale,
                MaybeHeaderOnly = resolved.MaybeHeaderOnly,
                Lines = lines
            },
            JsonOptions);

    private static string RenderText(
        ResolvedTarget resolved, int startLine, int endLine, string[] lines, bool truncated, bool stale, bool compact)
    {
        var builder = new StringBuilder();
        builder.Append(resolved.RelativePath).Append(':').Append(startLine).Append('-').Append(endLine);
        if (stale)
        {
            builder.Append("  [stale: source changed since compile; line numbers may no longer match]");
        }

        if (truncated)
        {
            builder.Append("  [truncated: pass --max-lines to read further]");
        }

        if (resolved.MaybeHeaderOnly)
        {
            builder.Append("  [this kernel only recorded the declaration line, not the body; open the file to read further]");
        }

        builder.AppendLine();
        if (!compact)
        {
            builder.AppendLine();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            builder.Append((startLine + i).ToString(CultureInfo.InvariantCulture).PadLeft(6)).Append("  ").AppendLine(lines[i]);
        }

        return builder.ToString().TrimEnd();
    }

    [GeneratedRegex(@"^(?<path>.+):(?<start>\d+)(-(?<end>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex FileRangePattern();

    private static async Task<int> Usage(TextWriter error, string message)
    {
        await error.WriteLineAsync($"error: {message}").ConfigureAwait(false);
        await error.WriteLineAsync(
            "usage: scout read \"<symbol>\"|\"<file>:<start>-<end>\" --kernel <path> [--repository <path>] [--max-lines <n>] [--json] [--format compact]")
            .ConfigureAwait(false);
        return ExitCodes.Usage;
    }
}

using System.Collections.ObjectModel;
using System.Globalization;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.Markdown;

/// <summary>
/// Extracts language-independent structural facts from Markdown documents.
/// </summary>
/// <remarks>
/// <para>
/// A document exposes one thing to the rest of a project: its outline. Each heading becomes a section symbol,
/// nested under the heading that encloses it, so a reader asking where something is documented is pointed at
/// the section to open rather than at the whole file.
/// </para>
/// <para>
/// Section bodies are never read. Following the rest of Scout, the kernel names where knowledge lives and the
/// reader opens it afterwards. Sections have no visibility, because a document does not declare one, and they
/// carry no cross-document references: whether a section is depended upon is not something prose establishes.
/// </para>
/// </remarks>
public sealed class MarkdownLanguageAnalyzer : ILanguageAnalyzer
{
    /// <summary>
    /// The native kind recorded on every document section.
    /// </summary>
    public const string SectionNativeKind = "markdown-section";

    private const string Id = "scout.analysis.markdown";
    private const string Language = "markdown";
    private const string PositionEncoding = "utf-16-code-units";
    private const string PathSeparator = " > ";

    private static readonly IReadOnlyList<string> Extensions = new ReadOnlyCollection<string>([".md", ".markdown"]);

    private static readonly string AnalyzerVersion =
        typeof(MarkdownLanguageAnalyzer).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Gets the stable identifier used by the Markdown analyzer.
    /// </summary>
    public string AnalyzerId => Id;

    /// <summary>
    /// Gets the Markdown language identifier.
    /// </summary>
    public string LanguageId => Language;

    /// <summary>
    /// Gets the repository extensions the Markdown analyzer reads.
    /// </summary>
    public IReadOnlyList<string> SupportedFileExtensions => Extensions;

    /// <summary>
    /// Analyzes Markdown documents and returns only language-independent facts.
    /// </summary>
    /// <param name="repository">The deterministic repository discovery result.</param>
    /// <param name="cancellationToken">A token that cancels analysis.</param>
    /// <returns>The Markdown analyzer contribution.</returns>
    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var builder = new ContributionBuilder(Id);
        var diagnostics = new List<AnalysisDiagnostic>();
        var documents = repository.Files
            .Where(file => Extensions.Contains(Path.GetExtension(file.RelativePath), StringComparer.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File
                    .ReadAllTextAsync(Scout.Core.Repository.RepositoryPath.Resolve(repository.RootPath, document.RelativePath), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    "MARKDOWN_UNREADABLE_FILE",
                    $"Document '{document.RelativePath}' could not be read: {exception.Message}"));
                continue;
            }

            ReadDocument(builder, document.RelativePath, document.ContentHash, content);
        }

        var status = diagnostics.Count == 0
            ? KnownAnalyzerStatuses.Succeeded
            : KnownAnalyzerStatuses.Partial;
        return builder.Build(CreateMetadata(status), diagnostics);
    }

    private static void ReadDocument(ContributionBuilder builder, string relativePath, string contentHash, string content)
    {
        var fileId = builder.AddSourceFile(relativePath, Language, contentHash);
        var lineIndex = new LineIndex(content);

        // The open ancestors of the heading being read, outermost first.
        var ancestors = new List<(int Level, string Title, StructuralId Id)>();
        var seenPaths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var heading in MarkdownHeadingReader.Read(content))
        {
            while (ancestors.Count > 0 && ancestors[^1].Level >= heading.Level)
            {
                ancestors.RemoveAt(ancestors.Count - 1);
            }

            var path = string.Join(PathSeparator, ancestors.Select(ancestor => ancestor.Title).Append(heading.Title));

            // Two sections with the same outline path are still two places in the document.
            var occurrence = seenPaths.GetValueOrDefault(path) + 1;
            seenPaths[path] = occurrence;
            var identity = occurrence == 1
                ? path
                : string.Create(CultureInfo.InvariantCulture, $"{path} ({occurrence})");

            var location = lineIndex.CreateLocation(relativePath, heading.Offset, heading.Length, fileId);
            var sectionId = builder.AddSymbol(
                StructuralId.Create(KnownEntityKinds.Symbol, relativePath, $"#{identity}"),
                heading.Title,
                Language,
                KnownSymbolKinds.DocumentSection,
                SectionNativeKind,
                $"{relativePath}#{identity}",
                location,
                $"Document '{relativePath}' has section '{path}'.");

            builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, sectionId, location);
            if (ancestors.Count > 0)
            {
                builder.AddRelationship(KnownRelationshipKinds.Contains, ancestors[^1].Id, sectionId, location);
            }

            ancestors.Add((heading.Level, heading.Title, sectionId));
        }
    }

    private static AnalyzerRunMetadata CreateMetadata(string status)
    {
        return new AnalyzerRunMetadata(
            Id,
            "Scout Markdown Analyzer",
            AnalyzerVersion,
            Language,
            status,
            DateTimeOffset.UtcNow,
            PositionEncoding);
    }

    private static AnalysisDiagnostic CreateDiagnostic(string code, string message)
    {
        return new AnalysisDiagnostic(
            StructuralId.Create("diagnostic", Id, code, message),
            Id,
            KnownDiagnosticSeverities.Warning,
            code,
            message);
    }
}

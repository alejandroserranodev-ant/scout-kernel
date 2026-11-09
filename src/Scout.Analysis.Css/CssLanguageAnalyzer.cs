using System.Collections.ObjectModel;
using Scout.Core.Analysis;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Analysis.Css;

/// <summary>
/// Extracts language-independent structural facts from style sheets.
/// </summary>
/// <remarks>
/// <para>
/// CSS has no declarations that other languages import, so this analyzer reports the only two names style
/// sheets expose to the rest of a project: class names and custom properties. Both are addressed by name, which
/// is why they are published through <see cref="SharedSymbolKey"/>: a React component naming a class in
/// <c>className</c> resolves to the same symbol the style sheet declares.
/// </para>
/// <para>
/// Class names in CSS Modules are rewritten by the bundler and are therefore file-scoped, not global. Those
/// classes stay private to their file and are deliberately not published, because resolving them requires
/// following the importing module's binding rather than matching a name.
/// </para>
/// </remarks>
public sealed class CssLanguageAnalyzer : ILanguageAnalyzer
{
    /// <summary>
    /// The shared vocabulary that identifies a globally addressable style class.
    /// </summary>
    public const string ClassVocabulary = "css.class";

    /// <summary>
    /// The shared vocabulary that identifies a CSS custom property.
    /// </summary>
    public const string CustomPropertyVocabulary = "css.custom-property";

    private const string Id = "scout.analysis.css";
    private const string Language = "css";
    private const string PositionEncoding = "utf-16-code-units";
    private const string ModuleSuffix = ".module.css";

    private static readonly IReadOnlyList<string> Extensions = new ReadOnlyCollection<string>([".css"]);

    private static readonly string AnalyzerVersion =
        typeof(CssLanguageAnalyzer).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <summary>
    /// Gets the stable identifier used by the CSS analyzer.
    /// </summary>
    public string AnalyzerId => Id;

    /// <summary>
    /// Gets the CSS language identifier.
    /// </summary>
    public string LanguageId => Language;

    /// <summary>
    /// Gets the repository extensions the CSS analyzer reads.
    /// </summary>
    public IReadOnlyList<string> SupportedFileExtensions => Extensions;

    /// <summary>
    /// Analyzes style sheets and returns only language-independent facts.
    /// </summary>
    /// <param name="repository">The deterministic repository discovery result.</param>
    /// <param name="cancellationToken">A token that cancels analysis.</param>
    /// <returns>The CSS analyzer contribution.</returns>
    public async Task<LanguageAnalysisResult> AnalyzeAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var builder = new ContributionBuilder(Id);
        var diagnostics = new List<AnalysisDiagnostic>();
        var styleSheets = repository.Files
            .Where(file => Path.GetExtension(file.RelativePath).Equals(".css", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var styleSheet in styleSheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            try
            {
                content = await File
                    .ReadAllTextAsync(Scout.Core.Repository.RepositoryPath.Resolve(repository.RootPath, styleSheet.RelativePath), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                diagnostics.Add(CreateDiagnostic(
                    "CSS_UNREADABLE_FILE",
                    $"Style sheet '{styleSheet.RelativePath}' could not be read: {exception.Message}"));
                continue;
            }

            ReadStyleSheet(builder, styleSheet.RelativePath, styleSheet.ContentHash, content);
        }

        var status = diagnostics.Count == 0
            ? KnownAnalyzerStatuses.Succeeded
            : KnownAnalyzerStatuses.Partial;
        return builder.Build(CreateMetadata(status), diagnostics);
    }

    private static void ReadStyleSheet(ContributionBuilder builder, string relativePath, string contentHash, string content)
    {
        var fileId = builder.AddSourceFile(relativePath, Language, contentHash);
        var lineIndex = new LineIndex(content);
        var isModule = relativePath.EndsWith(ModuleSuffix, StringComparison.OrdinalIgnoreCase);

        foreach (var rule in CssStyleSheetReader.Read(content))
        {
            var location = lineIndex.CreateLocation(relativePath, rule.SelectorOffset, rule.Selector.Length, fileId);
            var classIds = new List<StructuralId>();

            foreach (var className in CssNameReader.ReadClassNames(rule.Selector))
            {
                var classId = isModule
                    ? builder.AddSymbol(
                        StructuralId.Create(KnownEntityKinds.Symbol, relativePath, $".{className}"),
                        className,
                        Language,
                        KnownSymbolKinds.Type,
                        KnownVisibilities.Private,
                        "css-class",
                        $"{relativePath}::.{className}",
                        location,
                        $"Style sheet '{relativePath}' declares file-scoped class '.{className}'.")
                    : builder.AddSymbol(
                        SharedSymbolKey.CreateId(ClassVocabulary, className),
                        className,
                        Language,
                        KnownSymbolKinds.Type,
                        KnownVisibilities.Public,
                        "css-class",
                        SharedSymbolKey.Create(ClassVocabulary, className),
                        location,
                        $"Style sheet '{relativePath}' declares class '.{className}'.");

                classIds.Add(classId);
                builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, classId, location);
            }

            foreach (var propertyName in CssNameReader.ReadDeclaredCustomProperties(rule.Body))
            {
                var propertyId = AddCustomProperty(builder, relativePath, propertyName, location);
                builder.AddRelationship(KnownRelationshipKinds.Defines, fileId, propertyId, location);
            }

            foreach (var propertyName in CssNameReader.ReadConsumedCustomProperties(rule.Body))
            {
                var propertyId = AddCustomProperty(builder, relativePath, propertyName, location);
                foreach (var classId in classIds)
                {
                    builder.AddRelationship(KnownRelationshipKinds.References, classId, propertyId, location);
                }
            }
        }
    }

    private static StructuralId AddCustomProperty(
        ContributionBuilder builder,
        string relativePath,
        string propertyName,
        SourceLocation location)
    {
        return builder.AddSymbol(
            SharedSymbolKey.CreateId(CustomPropertyVocabulary, propertyName),
            propertyName,
            Language,
            KnownSymbolKinds.Value,
            KnownVisibilities.Public,
            "css-custom-property",
            SharedSymbolKey.Create(CustomPropertyVocabulary, propertyName),
            location,
            $"Style sheet '{relativePath}' uses custom property '--{propertyName}'.");
    }

    private static AnalyzerRunMetadata CreateMetadata(string status)
    {
        return new AnalyzerRunMetadata(
            Id,
            "Scout CSS Analyzer",
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

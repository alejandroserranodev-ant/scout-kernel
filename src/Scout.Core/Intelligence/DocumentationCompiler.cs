using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// Derives the documented intent a repository's own documentation states.
/// </summary>
/// <remarks>
/// <para>
/// This compiler reads only symbols of kind <see cref="KnownSymbolKinds.DocumentSection"/>, so it knows no
/// document format: any analyzer that reports sections feeds it. A section whose title names a topic becomes
/// a <see cref="DocumentedIntent"/>; the body is never consulted, so the claim is a pointer to where the
/// project speaks about the topic and nothing about what it says.
/// </para>
/// <para>
/// Confidence is deliberately modest and constant. A title matching a vocabulary is weak evidence that the
/// section covers the topic, and says nothing about whether the section is still true.
/// </para>
/// </remarks>
internal static class DocumentationCompiler
{
    private const double TitleMatchConfidence = 0.5;

    private static readonly string[] DocumentationRootNames = ["docs", "doc", "documentation"];

    internal static DocumentedIntent[] Compile(StructuralSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var intents = new List<DocumentedIntent>();
        foreach (var section in snapshot.Symbols.Where(symbol => symbol.SymbolKind == KnownSymbolKinds.DocumentSection))
        {
            var location = section.DeclarationLocations.Count > 0 ? section.DeclarationLocations[0] : null;
            if (location is null)
            {
                continue;
            }

            var scope = ResolveScope(location.RelativePath);
            var path = section.QualifiedName ?? section.Name;
            foreach (var topic in DocumentationTopicClassifier.Classify(section.Name))
            {
                intents.Add(new DocumentedIntent(
                    StructuralId.Create("documented-intent", topic, section.Id.Value),
                    topic,
                    section.Id,
                    section.Name,
                    path,
                    location.RelativePath,
                    location.StartLine,
                    $"Section '{section.Name}' is titled for {topic}. Only its title was read, so what it states is unverified.",
                    new KnowledgeMetadata(
                        KnowledgeClassification.Recommendation,
                        new ConfidenceScore(TitleMatchConfidence),
                        scope,
                        KnowledgeFreshness.Unknown,
                        section.EvidenceIds)));
            }
        }

        return [.. intents.OrderBy(intent => intent.Id)];
    }

    /// <summary>
    /// Describes how many documented sections each topic has, in reporting order.
    /// </summary>
    /// <param name="intents">The documented intents to summarize.</param>
    /// <returns>A comma-separated list such as <c>architecture 3, conventions 2</c>.</returns>
    internal static string DescribeTopics(IEnumerable<DocumentedIntent> intents)
    {
        ArgumentNullException.ThrowIfNull(intents);

        var countsByTopic = intents
            .GroupBy(intent => intent.Topic, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return string.Join(
            ", ",
            KnownDocumentationTopics.All
                .Where(countsByTopic.ContainsKey)
                .Select(topic => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{topic} {countsByTopic[topic]}")));
    }

    // Documentation usually lives in a folder named for it and speaks for the code around that folder, so
    // the scope is the directory that holds the folder rather than the folder itself.
    private static string ResolveScope(string documentPath)
    {
        var directory = RepositoryScope.DirectoryOf(documentPath);
        if (directory == ".")
        {
            return RepositoryScope.EntireRepository;
        }

        var segments = directory.Split('/');
        var rootIndex = Array.FindIndex(
            segments,
            segment => DocumentationRootNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
        var scopeSegments = rootIndex < 0 ? segments : segments[..rootIndex];
        return scopeSegments.Length == 0
            ? RepositoryScope.EntireRepository
            : $"{string.Join('/', scopeSegments)}/**";
    }
}

using System.Globalization;
using Scout.Core.Intelligence;
using Scout.Core.Ir;

namespace Scout.Core.Retrieval;

/// <summary>
/// Projects a compiled kernel into the corpus it is retrieved through.
/// </summary>
/// <remarks>
/// Cards are projections of knowledge Scout already established; this compiler infers nothing new. Source
/// text is deliberately not indexed: retrieving a body would return the tokens the kernel exists to avoid,
/// and the body is what the agent opens afterwards, once retrieval has told it which one to open.
/// </remarks>
public static class RetrievalCardCompiler
{
    // Ranking is similarity first. Importance only decides between cards a query matches equally well, and
    // there the boundary that breaks callers is worth more than the declaration that merely sits nearby.
    private const double ContractImportance = 1.0;
    private const double ModuleImportance = 0.6;
    private const double PublicSymbolImportance = 0.4;
    private const double SymbolImportance = 0.2;

    // Documentation is stated intent, so a section titled for a topic outranks a plain symbol but never a
    // contract, and a section that names no topic stays below every symbol.
    private const double DocumentedTopicImportance = 0.5;
    private const double DocumentationImportance = 0.1;
    private const string DocumentationDetail = "documentation/index.json";

    // Wording only points at the declaration that holds it, so it ranks below the declaration itself and above
    // an undocumented section: a query for a name must not be answered with the texts that happen to mention it.
    private const double TextImportance = 0.15;

    // A symbol names the declarations that read it, so a question about what is shown can reach the reader. The
    // cap keeps a widely used symbol from drowning its own name in the names around it.
    private const int MaxReadersPerCard = 8;

    // A declaration names what it reads for the same reason: the field it shows is how a question about the
    // screen reaches it. A component reads more than a field does, so it gets more room: on a 500-file application the 90th percentile reads 13 names.
    private const int MaxReadsPerCard = 24;

    // Among symbols a query matches equally well, the one more declarations read is the one that matters.
    private const double ReaderImportanceStep = 0.02;

    // A file profile is the answer to "where do I edit", so it ranks between a declaration and its wording. It is
    // bounded on every axis: a generated style sheet or a dictionary file must not turn into a card of its own size.
    private const double FileImportance = 0.3;
    private const int MaxDeclarationsPerFile = 20;
    private const int MaxReadsPerFile = 40;
    private const int MaxTextsPerFile = 15;
    private const int ReaderImportanceCap = 10;

    private static readonly string[] ReadingRelationshipKinds =
    [
        KnownRelationshipKinds.Calls,
        KnownRelationshipKinds.References,
        KnownRelationshipKinds.Renders
    ];

    /// <summary>
    /// Compiles the retrieval corpus.
    /// </summary>
    /// <param name="structural">The structural source of truth.</param>
    /// <param name="intelligence">The intelligence derived from it.</param>
    /// <returns>The cards, ordered deterministically by kind and identifier.</returns>
    public static IReadOnlyList<RetrievalCard> Compile(
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence)
    {
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(intelligence);

        var contractedSymbolIds = intelligence.Contracts.Select(contract => contract.SymbolId).ToHashSet();
        var fileHashesByPath = structural.SourceFiles
            .Where(file => file.ContentHash is not null)
            .ToDictionary(file => file.RelativePath, file => (file.ContentHash!, file.HashAlgorithm!), StringComparer.Ordinal);
        var symbolsById = structural.Symbols.ToDictionary(symbol => symbol.Id);

        var cards = new List<RetrievalCard>(
            intelligence.Contracts.Count + intelligence.Modules.Count + structural.Symbols.Count);
        cards.AddRange(intelligence.Contracts.Select(contract =>
            CreateContractCard(contract, symbolsById, fileHashesByPath)));
        cards.AddRange(intelligence.Modules.Select(CreateModuleCard));

        var topicsBySection = intelligence.DocumentedIntents
            .GroupBy(intent => intent.SectionSymbolId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(intent => intent.Topic).Distinct(StringComparer.Ordinal).ToArray());

        var holdersByText = FindTextHolders(structural);
        var readersBySymbol = FindReaders(structural);
        var readsBySymbol = FindReads(structural);

        // A symbol that is already a contract would otherwise compete with its own richer card and split the
        // similarity between two near-identical entries.
        cards.AddRange(structural.Symbols
            .Where(symbol => !contractedSymbolIds.Contains(symbol.Id))
            .Select(symbol => symbol.SymbolKind switch
            {
                KnownSymbolKinds.DocumentSection => CreateDocumentationCard(
                    symbol,
                    topicsBySection.GetValueOrDefault(symbol.Id, [])),
                KnownSymbolKinds.TextLiteral => CreateTextCard(symbol, holdersByText.GetValueOrDefault(symbol.Id)),
                _ => CreateSymbolCard(
                    symbol,
                    readersBySymbol.GetValueOrDefault(symbol.Id, []),
                    readsBySymbol.GetValueOrDefault(symbol.Id, []),
                    fileHashesByPath)
            }));

        cards.AddRange(CreateFileCards(structural, readsBySymbol, fileHashesByPath));

        return cards
            .OrderBy(card => card.Kind, StringComparer.Ordinal)
            .ThenBy(card => card.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static RetrievalCard CreateContractCard(
        ContractIntelligence contract,
        Dictionary<StructuralId, SymbolFact> symbolsById,
        IReadOnlyDictionary<string, (string Hash, string Algorithm)> fileHashesByPath)
    {
        var facets = new List<RetrievalFacet>
        {
            new("contractKind", contract.ContractKind),
            new("symbolKind", contract.SymbolKind),
            new("visibility", contract.Visibility),
            new("blastRadius", contract.BlastRadius.ToString(CultureInfo.InvariantCulture)),
            new("confidence", contract.Metadata.Confidence.Value.ToString("0.00", CultureInfo.InvariantCulture))
        };
        if (contract.QualifiedName is not null)
        {
            facets.Add(new RetrievalFacet("qualifiedName", contract.QualifiedName));
        }

        // A contract is a symbol promoted to a richer card (see the exclusion below), not a different kind of
        // entity, so `scout read`/`scout brief` resolve it to source the same way: its own declaration location.
        if (symbolsById.TryGetValue(contract.SymbolId, out var symbol) && symbol.DeclarationLocations.Count > 0)
        {
            var location = symbol.DeclarationLocations[0];
            facets.Add(new RetrievalFacet(KnownFacetNames.Line, location.StartLine.ToString(CultureInfo.InvariantCulture)));
            facets.Add(new RetrievalFacet(KnownFacetNames.EndLine, location.EndLine.ToString(CultureInfo.InvariantCulture)));
            AddStalenessFacets(facets, location.RelativePath, fileHashesByPath);
        }

        return new RetrievalCard(
            contract.Id.Value,
            KnownCardKinds.Contract,
            contract.Name,
            contract.RelativePath,
            contract.Summary,
            ContractImportance,
            facets,
            contract.Consumers.Select(consumer => consumer.Name),
            $"contracts/{ToFileStem(contract.Id)}.json");
    }

    private static RetrievalCard CreateModuleCard(ModuleIntelligence module)
    {
        return new RetrievalCard(
            module.Id.Value,
            KnownCardKinds.Module,
            module.Name,
            module.RelativePath,
            module.Summary,
            ModuleImportance,
            [
                new RetrievalFacet("sourceFiles", module.SourceFileCount.ToString(CultureInfo.InvariantCulture)),
                new RetrievalFacet("symbols", module.SymbolCount.ToString(CultureInfo.InvariantCulture)),
                new RetrievalFacet("scope", module.Metadata.Scope)
            ],
            related: null,
            detail: $"modules/{ToFileStem(module.Id)}.json");
    }

    private static RetrievalCard CreateSymbolCard(
        SymbolFact symbol,
        string[] readers,
        string[] reads,
        IReadOnlyDictionary<string, (string Hash, string Algorithm)> fileHashesByPath)
    {
        var location = symbol.DeclarationLocations.Count > 0 ? symbol.DeclarationLocations[0] : null;
        var facets = new List<RetrievalFacet>
        {
            new("symbolKind", symbol.SymbolKind),
            new("visibility", symbol.Visibility)
        };
        if (symbol.NativeKind is not null)
        {
            facets.Add(new RetrievalFacet("nativeKind", symbol.NativeKind));
        }

        if (location is not null)
        {
            facets.Add(new RetrievalFacet(
                KnownFacetNames.Line,
                location.StartLine.ToString(CultureInfo.InvariantCulture)));
            facets.Add(new RetrievalFacet(
                KnownFacetNames.EndLine,
                location.EndLine.ToString(CultureInfo.InvariantCulture)));
            AddStalenessFacets(facets, location.RelativePath, fileHashesByPath);
        }

        var isPublic = string.Equals(symbol.Visibility, KnownVisibilities.Public, StringComparison.Ordinal);
        var usage = Math.Min(readers.Length, ReaderImportanceCap) * ReaderImportanceStep;
        string[] neighbours = [.. readers, .. reads];
        return new RetrievalCard(
            symbol.Id.Value,
            KnownCardKinds.Symbol,
            symbol.Name,
            location?.RelativePath ?? string.Empty,
            BuildSymbolSummary(symbol, location),
            (isPublic ? PublicSymbolImportance : SymbolImportance) + usage,
            facets,
            symbol.QualifiedName is null ? neighbours : [symbol.QualifiedName, .. neighbours]);
    }

    // Lets a caller holding only the retrieval index (never the much larger structural IR) tell whether the file
    // a card points at has changed since compile, without reading anything but this card's own facets.
    private static void AddStalenessFacets(
        List<RetrievalFacet> facets,
        string relativePath,
        IReadOnlyDictionary<string, (string Hash, string Algorithm)> fileHashesByPath)
    {
        if (fileHashesByPath.TryGetValue(relativePath, out var hash))
        {
            facets.Add(new RetrievalFacet(KnownFacetNames.ContentHash, hash.Hash));
            facets.Add(new RetrievalFacet(KnownFacetNames.HashAlgorithm, hash.Algorithm));
        }
    }

    private static RetrievalCard CreateDocumentationCard(SymbolFact section, string[] topics)
    {
        var location = section.DeclarationLocations.Count > 0 ? section.DeclarationLocations[0] : null;
        var facets = topics.Select(topic => new RetrievalFacet("topic", topic)).ToList();
        if (location is not null)
        {
            facets.Add(new RetrievalFacet(
                KnownFacetNames.Line,
                location.StartLine.ToString(CultureInfo.InvariantCulture)));
        }

        var where = location is null
            ? "no recorded location"
            : string.Create(CultureInfo.InvariantCulture, $"{location.RelativePath}:{location.StartLine}");
        var summary = topics.Length == 0
            ? $"documentation section '{section.Name}', at {where}."
            : $"documented {string.Join(", ", topics)} section '{section.Name}', at {where}. Stated intent: only the title was read.";
        return new RetrievalCard(
            section.Id.Value,
            KnownCardKinds.Documentation,
            section.Name,
            location?.RelativePath ?? string.Empty,
            summary,
            topics.Length == 0 ? DocumentationImportance : DocumentedTopicImportance,
            facets,
            section.QualifiedName is null ? null : [section.QualifiedName],
            topics.Length == 0 ? null : DocumentationDetail);
    }

    // One card per source file that declares something. It gathers what the file declares, what its callables read
    // and the wording it shows, because a question about behaviour is usually phrased in those words and the answer
    // is the file to open, not one of the many same-named symbols spread across the repository.
    private static IEnumerable<RetrievalCard> CreateFileCards(
        StructuralSnapshot structural,
        IReadOnlyDictionary<StructuralId, string[]> readsBySymbol,
        IReadOnlyDictionary<string, (string Hash, string Algorithm)> fileHashesByPath)
    {
        var symbolsByFile = structural.Symbols
            .Where(symbol => symbol.DeclarationLocations.Count > 0)
            .GroupBy(symbol => symbol.DeclarationLocations[0].RelativePath, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var file in symbolsByFile)
        {
            var declared = file
                .Where(symbol => symbol.SymbolKind is not (KnownSymbolKinds.TextLiteral or KnownSymbolKinds.DocumentSection))
                .ToArray();
            if (declared.Length == 0)
            {
                continue;
            }

            var texts = file
                .Where(symbol => symbol.SymbolKind == KnownSymbolKinds.TextLiteral)
                .OrderBy(symbol => symbol.DeclarationLocations[0].StartLine)
                .ThenBy(symbol => symbol.DeclarationLocations[0].StartColumn)
                .ToArray();
            var reads = declared
                .SelectMany(symbol => readsBySymbol.GetValueOrDefault(symbol.Id, []))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(MaxReadsPerFile);
            var names = declared
                .Select(symbol => symbol.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Take(MaxDeclarationsPerFile);

            var fileFacets = new List<RetrievalFacet>
            {
                new("declarations", declared.Length.ToString(CultureInfo.InvariantCulture)),
                new("texts", texts.Length.ToString(CultureInfo.InvariantCulture))
            };
            AddStalenessFacets(fileFacets, file.Key, fileHashesByPath);

            var separator = file.Key.LastIndexOf('/');
            yield return new RetrievalCard(
                StructuralId.Create("file-card", file.Key).Value,
                KnownCardKinds.File,
                separator < 0 ? file.Key : file.Key[(separator + 1)..],
                file.Key,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"source file {file.Key} with {declared.Length} declarations and {texts.Length} texts."),
                FileImportance,
                fileFacets,
                names.Concat(reads).Concat(texts.Take(MaxTextsPerFile).Select(text => text.Name)));
        }
    }

    // The names of the declarations that call, reference or render each symbol, as the graph observed them.
    private static Dictionary<StructuralId, string[]> FindReaders(StructuralSnapshot structural)
    {
        var symbolsById = structural.Symbols.ToDictionary(symbol => symbol.Id);
        var readers = new Dictionary<StructuralId, SortedSet<string>>();
        foreach (var relationship in structural.Relationships)
        {
            if (!ReadingRelationshipKinds.Contains(relationship.Kind, StringComparer.Ordinal) ||
                relationship.SourceId == relationship.TargetId ||
                !symbolsById.TryGetValue(relationship.SourceId, out var reader) ||
                !symbolsById.ContainsKey(relationship.TargetId))
            {
                continue;
            }

            if (!readers.TryGetValue(relationship.TargetId, out var names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                readers[relationship.TargetId] = names;
            }

            names.Add(reader.Name);
        }

        return readers.ToDictionary(entry => entry.Key, entry => entry.Value.Take(MaxReadersPerCard).ToArray());
    }

    // The names of what each declaration calls, references or renders. Style classes are left out: they are
    // another language's vocabulary and would crowd out the fields and functions the declaration works with.
    private static Dictionary<StructuralId, string[]> FindReads(StructuralSnapshot structural)
    {
        var symbolsById = structural.Symbols.ToDictionary(symbol => symbol.Id);
        var reads = new Dictionary<StructuralId, SortedSet<string>>();
        foreach (var relationship in structural.Relationships)
        {
            if (!ReadingRelationshipKinds.Contains(relationship.Kind, StringComparer.Ordinal) ||
                relationship.SourceId == relationship.TargetId ||
                !symbolsById.TryGetValue(relationship.SourceId, out var reader) ||
                reader.SymbolKind != KnownSymbolKinds.Callable ||
                !symbolsById.TryGetValue(relationship.TargetId, out var target) ||
                target.SymbolKind is KnownSymbolKinds.Type ||
                string.Equals(target.LanguageId, "css", StringComparison.Ordinal))
            {
                continue;
            }

            if (!reads.TryGetValue(relationship.SourceId, out var names))
            {
                names = new SortedSet<string>(StringComparer.Ordinal);
                reads[relationship.SourceId] = names;
            }

            names.Add(target.Name);
        }

        return reads.ToDictionary(entry => entry.Key, entry => entry.Value.Take(MaxReadsPerCard).ToArray());
    }

    // The symbol that holds a text is the one that contains it: that is the declaration the agent opens next.
    private static Dictionary<StructuralId, SymbolFact> FindTextHolders(StructuralSnapshot structural)
    {
        var symbolsById = structural.Symbols.ToDictionary(symbol => symbol.Id);
        var holders = new Dictionary<StructuralId, SymbolFact>();
        foreach (var relationship in structural.Relationships)
        {
            if (relationship.Kind == KnownRelationshipKinds.Contains &&
                symbolsById.TryGetValue(relationship.TargetId, out var text) &&
                text.SymbolKind == KnownSymbolKinds.TextLiteral &&
                symbolsById.TryGetValue(relationship.SourceId, out var holder))
            {
                holders[text.Id] = holder;
            }
        }

        return holders;
    }

    private static RetrievalCard CreateTextCard(SymbolFact text, SymbolFact? holder)
    {
        var location = text.DeclarationLocations.Count > 0 ? text.DeclarationLocations[0] : null;
        var facets = new List<RetrievalFacet>();
        if (text.NativeKind is not null)
        {
            facets.Add(new RetrievalFacet("nativeKind", text.NativeKind));
        }

        if (location is not null)
        {
            facets.Add(new RetrievalFacet(
                KnownFacetNames.Line,
                location.StartLine.ToString(CultureInfo.InvariantCulture)));
        }

        var where = location is null
            ? "no recorded location"
            : string.Create(CultureInfo.InvariantCulture, $"{location.RelativePath}:{location.StartLine}");
        var inside = holder is null ? string.Empty : $" in '{holder.Name}'";
        return new RetrievalCard(
            text.Id.Value,
            KnownCardKinds.Text,
            text.Name,
            location?.RelativePath ?? string.Empty,
            $"{text.NativeKind ?? "text"} '{text.Name}'{inside}, at {where}.",
            TextImportance,
            facets,
            holder is null ? null : [holder.Name]);
    }

    private static string BuildSymbolSummary(SymbolFact symbol, SourceLocation? location)
    {
        var where = location is null
            ? "no recorded declaration location"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"declared at {location.RelativePath}:{location.StartLine}");
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{symbol.Visibility} {symbol.NativeKind ?? symbol.SymbolKind} '{symbol.Name}', {where}.");
    }

    // Mirrors the kernel file layout so a card can point at the record it was projected from.
    private static string ToFileStem(StructuralId id)
    {
        return string.Create(id.Value.Length, id.Value, static (span, value) =>
        {
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                span[index] = char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                    ? character
                    : '-';
            }
        });
    }
}

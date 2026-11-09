using System.Globalization;
using Scout.Core.Intelligence;
using Scout.Core.Ir;

namespace Scout.Core.Agents;

/// <summary>
/// Specializes the standard multi-agent roles against one compiled kernel.
/// </summary>
/// <remarks>
/// <para>
/// A generic role prompt tells an agent what a reviewer is. A specialized one tells it what reviewing
/// <em>this</em> repository means: which boundaries exist, whether conventions were established at all, and
/// what is still unknown. The difference is that the second cannot be satisfied by plausible-sounding output.
/// </para>
/// <para>
/// Specialization is drawn only from what the kernel established. Where the kernel knows nothing, the
/// template says so instead of inventing an obligation the agent cannot discharge.
/// </para>
/// </remarks>
public static class AgentTemplateCompiler
{
    private const string ArchitectId = "architect";
    private const string DeveloperId = "developer";
    private const string ReviewerId = "reviewer";
    private const string QualityId = "qa";

    /// <summary>
    /// Compiles the role templates for a kernel.
    /// </summary>
    /// <param name="structural">The structural source of truth.</param>
    /// <param name="intelligence">The intelligence derived from it.</param>
    /// <param name="retrievalPath">The kernel-relative path of the retrieval index, when one was published.</param>
    /// <returns>The templates, ordered by the sequence they are normally run in.</returns>
    public static IReadOnlyList<AgentTemplate> Compile(
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        string? retrievalPath)
    {
        ArgumentNullException.ThrowIfNull(structural);
        ArgumentNullException.ThrowIfNull(intelligence);

        var contractCount = intelligence.Contracts.Count;
        var moduleCount = intelligence.Modules.Count;
        var conventionCount = intelligence.Conventions.Count;
        var languages = structural.Projects
            .Select(project => project.LanguageId)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var languageList = languages.Length == 0 ? "no identified language" : string.Join(", ", languages);

        var retrievalNote = retrievalPath is null
            ? "No retrieval index was published; read the kernel index files directly."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Query '{retrievalPath}' before opening any file; open source only for what it returns.");

        var documentation = new DocumentationBrief(
            structural.Symbols.Count(symbol => symbol.SymbolKind == KnownSymbolKinds.DocumentSection),
            intelligence.DocumentedIntents);

        var locating = new LocatingBrief(
            structural.Symbols.Any(symbol => symbol.SymbolKind != KnownSymbolKinds.DocumentSection),
            structural.Symbols.Any(symbol => symbol.SymbolKind == KnownSymbolKinds.TextLiteral));

        return
        [
            CompileArchitect(moduleCount, contractCount, languageList, retrievalNote, intelligence.UnknownAreas, documentation, locating),
            CompileDeveloper(contractCount, conventionCount, languageList, retrievalNote, documentation, locating),
            CompileReviewer(contractCount, conventionCount, retrievalNote, documentation, locating),
            CompileQuality(structural, contractCount, retrievalNote, documentation, locating)
        ];
    }

    private static AgentTemplate CompileArchitect(
        int moduleCount,
        int contractCount,
        string languageList,
        string retrievalNote,
        IReadOnlyList<string> unknownAreas,
        DocumentationBrief documentation,
        LocatingBrief locating)
    {
        var obligations = new List<string>
        {
            retrievalNote,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Name the module the change belongs to, out of the {moduleCount} the kernel established, and say why."),
            string.Create(
                CultureInfo.InvariantCulture,
                $"List every one of the {contractCount} contracts the change would cross, with its blast radius.")
        };

        obligations.Add(unknownAreas.Count == 0
            ? "The kernel records no unknown areas; do not invent one."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The kernel is explicit that {string.Join("; ", unknownAreas)} remain unknown. If the change depends on any of them, say so and stop rather than guessing."));

        var architectReads = new List<string> { "KERNEL.md", "architecture/overview.md", "modules/index.json", "contracts/index.json" };
        var architectKinds = new List<string> { Retrieval.KnownCardKinds.Module, Retrieval.KnownCardKinds.Contract };
        locating.AddKinds(architectKinds);
        locating.AddObligation(obligations, LocatingBrief.ArchitectObligation);
        var designSections = documentation.Count(
            KnownDocumentationTopics.Architecture,
            KnownDocumentationTopics.Decisions,
            KnownDocumentationTopics.Flows);
        if (documentation.HasSections)
        {
            architectKinds.Add(Retrieval.KnownCardKinds.Documentation);
            obligations.Add(DocumentationBrief.VerificationObligation);
            obligations.Add(designSections == 0
                ? "No documentation section is titled for architecture, decisions or flows; do not cite documentation for the design."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Read the {designSections} documented architecture, decision and flow sections that touch the change (retrieve them with --kind documentation). They are stated intent from the project's documents, not observed fact: cite them as such, and where they disagree with the modules and contracts above, report both and plan against the observed ones."));
        }

        var documentedUnknowns = unknownAreas
            .Where(area => documentation.Count(area) > 0)
            .ToArray();
        if (documentedUnknowns.Length > 0)
        {
            obligations.Add(
                $"The documents do speak about {string.Join(" and ", documentedUnknowns)}. That records what the project claims; it does not make the area known, so say which parts rest only on documentation.");
        }

        if (documentation.HasIntents)
        {
            architectReads.Add(DocumentationBrief.IndexPath);
        }

        var architectQueries = new List<AutoQuery>();
        if (designSections > 0)
        {
            architectQueries.Add(new AutoQuery("architecture design decisions and flows", Retrieval.KnownCardKinds.Documentation, 6));
        }

        if (moduleCount > 0)
        {
            architectQueries.Add(new AutoQuery("main modules and their dependencies", Retrieval.KnownCardKinds.Module, 5));
        }
        if (contractCount > 0)
        {
            architectQueries.Add(new AutoQuery("highest-impact contracts and stability boundaries", Retrieval.KnownCardKinds.Contract, 8));
        }

        return new AgentTemplate(
            ArchitectId,
            "Architect",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Decide where a change belongs in a {languageList} repository of {moduleCount} modules, and which established boundaries it crosses. Produce a plan; write no code."),
            architectReads,
            architectKinds,
            retrievalLimit: 8,
            obligations,
            [
                new AgentHandoff(
                    DeveloperId,
                    KnownHandoffModes.Sequential,
                    "The affected modules and crossed contracts are identified.")
            ],
            architectQueries);
    }

    private static AgentTemplate CompileDeveloper(
        int contractCount,
        int conventionCount,
        string languageList,
        string retrievalNote,
        DocumentationBrief documentation,
        LocatingBrief locating)
    {
        var obligations = new List<string>
        {
            retrievalNote,
            "Load the contract record for every symbol you are about to change, before changing it.",
            "Update every consumer a contract lists. The consumer list is observed, not estimated: an unhandled consumer is a break."
        };

        obligations.Add(conventionCount == 0
            ? "The kernel established no conventions. Follow the surrounding file and do not claim a repository-wide rule."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Follow the {conventionCount} conventions in 'conventions/index.json'; each one carries the evidence it was derived from."));

        var developerReads = new List<string> { "KERNEL.md", "contracts/index.json", "conventions/index.json" };
        var developerKinds = new List<string> { Retrieval.KnownCardKinds.Contract, Retrieval.KnownCardKinds.Symbol };
        locating.AddKinds(developerKinds);
        locating.AddObligation(obligations, LocatingBrief.DeveloperObligation);
        var documentedConventions = documentation.Count(
            KnownDocumentationTopics.Conventions,
            KnownDocumentationTopics.Operations);
        if (documentation.HasSections)
        {
            developerKinds.Add(Retrieval.KnownCardKinds.Documentation);
            obligations.Add(DocumentationBrief.VerificationObligation);
            obligations.Add(documentedConventions == 0
                ? "No documentation section is titled for conventions or operations; do not cite documentation for how to write or run the change."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Read the {documentedConventions} documented convention and operations sections for the area you change (retrieve them with --kind documentation). Follow them unless they contradict an observed convention or contract; then follow the observation and say so."));
        }

        if (documentation.HasIntents)
        {
            developerReads.Add(DocumentationBrief.IndexPath);
        }

        var developerQueries = new List<AutoQuery>();
        if (documentedConventions > 0)
        {
            developerQueries.Add(new AutoQuery("coding conventions style and setup guidelines", Retrieval.KnownCardKinds.Documentation, 5));
        }

        if (contractCount > 0)
        {
            developerQueries.Add(new AutoQuery("contracts with high consumer counts that require careful handling", Retrieval.KnownCardKinds.Contract, 6));
            developerQueries.Add(new AutoQuery("commonly-changed symbols and their impact", Retrieval.KnownCardKinds.Symbol, 5));
        }

        return new AgentTemplate(
            DeveloperId,
            "Developer",
            string.Create(
                CultureInfo.InvariantCulture,
                $"Implement the planned change in {languageList} without breaking any of the {contractCount} established contracts."),
            developerReads,
            developerKinds,
            retrievalLimit: 10,
            obligations,
            [
                new AgentHandoff(
                    ArchitectId,
                    KnownHandoffModes.Subsession,
                    "The change turns out to cross a boundary the plan did not anticipate."),
                new AgentHandoff(
                    ReviewerId,
                    KnownHandoffModes.Sequential,
                    "The change is complete and every listed consumer is handled.")
            ],
            developerQueries);
    }

    private static AgentTemplate CompileReviewer(
        int contractCount,
        int conventionCount,
        string retrievalNote,
        DocumentationBrief documentation,
        LocatingBrief locating)
    {
        var obligations = new List<string>
        {
            retrievalNote,
            string.Create(
                CultureInfo.InvariantCulture,
                $"For each changed symbol, retrieve its contract and confirm every consumer of the {contractCount} contracts was handled."),
            "Reject any claim the kernel does not support. 'Probably unused' is not a finding; an empty consumer list is.",
            "Distinguish a contract breach, which blocks, from a convention deviation, which is advisory."
        };

        if (conventionCount == 0)
        {
            obligations.Add("No conventions were established, so no deviation can be reported as one.");
        }

        var reviewerReads = new List<string> { "KERNEL.md", "contracts/index.json", "conventions/index.json" };
        var reviewerKinds = new List<string> { Retrieval.KnownCardKinds.Contract, Retrieval.KnownCardKinds.Symbol };
        locating.AddWordingKind(reviewerKinds);
        locating.AddWordingObligation(obligations, LocatingBrief.ReviewerObligation);
        var documentedRules = documentation.Count(
            KnownDocumentationTopics.Conventions,
            KnownDocumentationTopics.Concerns,
            KnownDocumentationTopics.Contracts);
        if (documentation.HasSections)
        {
            reviewerKinds.Add(Retrieval.KnownCardKinds.Documentation);
            obligations.Add(DocumentationBrief.VerificationObligation);
            obligations.Add(documentedRules == 0
                ? "No documentation section is titled for conventions, concerns or contracts, so no deviation can be reported against documentation."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"A deviation from the {documentedRules} documented convention, concern and contract sections is advisory unless it also breaches an observed contract. Cite the section, and never present documented intent as an observed contract."));
        }

        if (documentation.HasIntents)
        {
            reviewerReads.Add(DocumentationBrief.IndexPath);
        }

        var reviewerQueries = new List<AutoQuery>();
        if (documentedRules > 0)
        {
            reviewerQueries.Add(new AutoQuery("documented conventions concerns and constraints", Retrieval.KnownCardKinds.Documentation, 6));
        }

        if (contractCount > 0)
        {
            reviewerQueries.Add(new AutoQuery("affected contracts and their consumer lists", Retrieval.KnownCardKinds.Contract, 10));
            reviewerQueries.Add(new AutoQuery("related symbols that may have been impacted", Retrieval.KnownCardKinds.Symbol, 8));
        }

        return new AgentTemplate(
            ReviewerId,
            "Code Reviewer",
            "Verify the change against the kernel rather than against taste. Every objection must cite a contract, a convention, or an evidence record.",
            reviewerReads,
            reviewerKinds,
            retrievalLimit: 10,
            obligations,
            [
                new AgentHandoff(
                    DeveloperId,
                    KnownHandoffModes.Sequential,
                    "A contract breach or an unhandled consumer was found."),
                new AgentHandoff(
                    QualityId,
                    KnownHandoffModes.Sequential,
                    "No contract breach remains.")
            ],
            reviewerQueries);
    }

    private static AgentTemplate CompileQuality(
        StructuralSnapshot structural,
        int contractCount,
        string retrievalNote,
        DocumentationBrief documentation,
        LocatingBrief locating)
    {
        var testFiles = structural.SourceFiles
            .Count(file => file.RelativePath.Contains("test", StringComparison.OrdinalIgnoreCase)
                || file.RelativePath.Contains("spec", StringComparison.OrdinalIgnoreCase));

        var obligations = new List<string>
        {
            retrievalNote,
            "Derive the cases to exercise from the consumer list of each crossed contract, so coverage follows observed usage rather than imagination."
        };

        obligations.Add(testFiles == 0
            ? "No file in this repository is named as a test or spec. Say plainly that there is no suite to extend instead of assuming a command exists."
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{testFiles} source files are named as tests or specs; extend the ones covering the changed contracts."));

        var qaReads = new List<string> { "KERNEL.md", "contracts/index.json" };
        var qaKinds = new List<string> { Retrieval.KnownCardKinds.Contract };
        locating.AddWordingKind(qaKinds);
        locating.AddWordingObligation(obligations, LocatingBrief.QualityObligation);
        var documentedTesting = documentation.Count(KnownDocumentationTopics.Testing);
        if (documentation.HasSections)
        {
            qaKinds.Add(Retrieval.KnownCardKinds.Documentation);
            obligations.Add(DocumentationBrief.VerificationObligation);
            obligations.Add(documentedTesting == 0
                ? "No documentation section is titled for testing; do not cite documentation for how to verify the change."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Prefer the verification steps described by the {documentedTesting} documented testing sections over inventing commands, and report them as unverified until they have been run."));
        }

        if (documentation.HasIntents)
        {
            qaReads.Add(DocumentationBrief.IndexPath);
        }

        var qaQueries = new List<AutoQuery>();
        if (documentedTesting > 0)
        {
            qaQueries.Add(new AutoQuery("testing and verification procedures", Retrieval.KnownCardKinds.Documentation, 5));
        }

        if (contractCount > 0)
        {
            qaQueries.Add(new AutoQuery("contracts that were changed with their consumer lists", Retrieval.KnownCardKinds.Contract, 8));
        }

        return new AgentTemplate(
            QualityId,
            "QA",
            "Establish whether the change behaves as the plan claimed, and report what remains unverified.",
            qaReads,
            qaKinds,
            retrievalLimit: 8,
            obligations,
            [
                new AgentHandoff(
                    DeveloperId,
                    KnownHandoffModes.Sequential,
                    "A case derived from a contract's consumers fails.")
            ],
            qaQueries);
    }

    // What the kernel offers for finding the place to work from the task's own words: files to open and the wording
    // a repository shows. A role is only pointed at a card kind the kernel actually has.
    private sealed record LocatingBrief(bool HasFiles, bool HasWording)
    {
        internal const string ArchitectObligation =
            "Locate before you plan, using the words of the task itself. Wording the task quotes (a button, a label, a message) is retrieved with --kind text and names the component that shows it; behaviour to change is retrieved with --kind file and names the file to open. Text that comes from data instead of source is not in --kind text: query the field's name with --kind symbol and read the declarations it lists as usedBy.";

        internal const string DeveloperObligation =
            "Confirm the file before you edit it: retrieve the task's own words with --kind text or --kind file. Each text card is one place that shows that wording, so wording with several cards must be changed in each place, or the omission justified.";

        internal const string ReviewerObligation =
            "When the change alters user-visible wording, retrieve the old wording with --kind text: every card is a place that may still show it, and a place the change left out is a finding.";

        internal const string QualityObligation =
            "For each user-visible text the change touches, retrieve it with --kind text and verify every place that shows it, so a message fixed in one place is not left stale in another.";

        internal void AddKinds(List<string> kinds)
        {
            if (HasFiles)
            {
                kinds.Add(Retrieval.KnownCardKinds.File);
            }

            AddWordingKind(kinds);
        }

        internal void AddWordingKind(List<string> kinds)
        {
            if (HasWording)
            {
                kinds.Add(Retrieval.KnownCardKinds.Text);
            }
        }

        internal void AddObligation(List<string> obligations, string obligation)
        {
            if (HasFiles)
            {
                obligations.Add(obligation);
            }
        }

        internal void AddWordingObligation(List<string> obligations, string obligation)
        {
            if (HasWording)
            {
                obligations.Add(obligation);
            }
        }
    }

    // What the documentation of the repository offers a role, so a role is only told to consult documents that exist.
    private sealed record DocumentationBrief(int SectionCount, IReadOnlyList<DocumentedIntent> Intents)
    {
        internal const string IndexPath = "documentation/index.json";

        // Scout reads only titles, so checking a document against the code is left to the role, and only when
        // the work depends on that document: reading every section up front would spend what the kernel saves.
        internal const string VerificationObligation =
            "Verify documentation only when the task depends on it. If you would follow a documented section or cite it as fact, open its body and compare it with the code it describes (the observed contracts and conventions, the files it names), then report it as confirmed, as a discrepancy (what the document says, what the code shows, and where each is), or as not checkable. Skip documents the task does not depend on. If a document states a convention or concern that no observed convention covers, apply it to this task as a documented, unverified rule and say so.";

        internal bool HasSections => SectionCount > 0;

        internal bool HasIntents => Intents.Count > 0;

        internal int Count(params string[] topics)
        {
            return Intents.Count(intent => topics.Contains(intent.Topic, StringComparer.Ordinal));
        }
    }
}

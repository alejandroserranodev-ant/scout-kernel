using System.Globalization;
using System.Text;
using Scout.Core.Agents;
using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Core.Kernel;

internal static class KernelRenderer
{
    private const string KernelVersion = "0.3.0";
    private const string DocumentationIndexPath = "documentation/index.json";

    internal static IReadOnlyList<KernelArtifact> Render(
        RepositoryDescriptor repository,
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        string scoutVersion,
        RetrievalPublication? retrieval)
    {
        var moduleDetails = intelligence.Modules.ToDictionary(
            module => module.Id,
            module => $"modules/{ToFileStem(module.Id)}.json");
        var conventionDetails = intelligence.Conventions.ToDictionary(
            convention => convention.Id,
            convention => $"conventions/{ToFileStem(convention.Id)}.json");
        var contractDetails = intelligence.Contracts.ToDictionary(
            contract => contract.Id,
            contract => $"contracts/{ToFileStem(contract.Id)}.json");
        var requestedEvidenceIds = intelligence.Modules
            .SelectMany(module => module.Metadata.EvidenceIds)
            .Concat(intelligence.Conventions.SelectMany(convention => convention.Metadata.EvidenceIds))
            .Concat(intelligence.Contracts.SelectMany(contract => contract.Metadata.EvidenceIds))
            .Concat(intelligence.DocumentedIntents.SelectMany(intent => intent.Metadata.EvidenceIds))
            .Distinct()
            .Order()
            .ToArray();
        var evidenceById = structural.Evidence.ToDictionary(evidence => evidence.Id);
        var resolvedEvidence = requestedEvidenceIds
            .Where(evidenceById.ContainsKey)
            .Select(id => evidenceById[id])
            .ToArray();
        var unresolvedEvidence = requestedEvidenceIds.Where(id => !evidenceById.ContainsKey(id)).ToArray();
        var evidenceDetails = resolvedEvidence.ToDictionary(
            evidence => evidence.Id,
            evidence => $"evidence/{ToFileStem(evidence.Id)}.json");

        var agents = AgentTemplateCompiler.Compile(structural, intelligence, retrieval?.RelativePath);
        var agentDetails = agents.ToDictionary(agent => agent.Id, agent => $"agents/{agent.Id}.json", StringComparer.Ordinal);

        var artifacts = new List<KernelArtifact>
        {
            KernelArtifact.Text("AGENTS.md", RenderAgents(intelligence, retrieval)),
            KernelArtifact.Text("KERNEL.md", RenderKernel(repository, structural, intelligence, retrieval)),
            KernelArtifact.Text(KernelReport.RelativePath, KernelReport.Render(repository, structural, intelligence)),
            KernelArtifact.Text("architecture/overview.md", RenderArchitecture(structural, intelligence, moduleDetails)),
            KernelArtifact.Text("agents/index.json", RenderAgentIndex(agents, agentDetails)),
            KernelArtifact.Text("conventions/index.json", RenderConventionIndex(intelligence, conventionDetails)),
            KernelArtifact.Text("contracts/index.json", RenderContractIndex(intelligence, contractDetails)),
            KernelArtifact.Text("evidence/index.json", RenderEvidenceIndex(resolvedEvidence, evidenceDetails, unresolvedEvidence)),
            KernelArtifact.Text("manifest.json", RenderManifest(repository, structural, intelligence, scoutVersion, unresolvedEvidence.Length, retrieval)),
            KernelArtifact.Text("modules/index.json", RenderModuleIndex(intelligence, moduleDetails)),
            KernelArtifact.Text(".internal/intelligence.json", KernelJson.Serialize(intelligence)),
            KernelArtifact.Text(".internal/structural-ir.json", KernelJson.Serialize(structural)),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/SKILL.md", OrchestrationSkillArtifacts.SkillMarkdown),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/bin/scout-agents.mjs", OrchestrationSkillArtifacts.RunnerScript),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/bin/lib/agent-retrieval-executor.mjs", OrchestrationSkillArtifacts.AgentRetrievalExecutor),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/bin/lib/retrieval-engine.mjs", RetrievalQuerySkillArtifacts.RetrievalEngine),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/adapters/AGENTS.md.snippet.md", OrchestrationSkillArtifacts.CodexAdapter),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/adapters/copilot-instructions.snippet.md", OrchestrationSkillArtifacts.CopilotAdapter),
            KernelArtifact.Text($"{OrchestrationSkillArtifacts.RelativeDirectory}/adapters/cursor-rules.snippet.mdc", OrchestrationSkillArtifacts.CursorAdapter),
            KernelArtifact.Text($"{RetrievalQuerySkillArtifacts.RelativeDirectory}/SKILL.md", RetrievalQuerySkillArtifacts.SkillMarkdown),
            KernelArtifact.Text($"{RetrievalQuerySkillArtifacts.RelativeDirectory}/bin/scout-query.mjs", RetrievalQuerySkillArtifacts.RunnerScript),
            KernelArtifact.Text($"{RetrievalQuerySkillArtifacts.RelativeDirectory}/bin/lib/retrieval-engine.mjs", RetrievalQuerySkillArtifacts.RetrievalEngine)
        };

        if (intelligence.DocumentedIntents.Count > 0)
        {
            artifacts.Add(KernelArtifact.Text(DocumentationIndexPath, RenderDocumentationIndex(intelligence)));
        }

        artifacts.AddRange(agents.Select(agent =>
            KernelArtifact.Text(agentDetails[agent.Id], KernelJson.Serialize(agent))));

        if (retrieval is not null)
        {
            artifacts.Add(KernelArtifact.Binary(retrieval.RelativePath, retrieval.Content));
        }

        artifacts.AddRange(intelligence.Modules.Select(module =>
            KernelArtifact.Text(moduleDetails[module.Id], KernelJson.Serialize(new
            {
                module.Id,
                module.Name,
                module.Summary,
                module.SourceProjectId,
                module.RelativePath,
                module.SourceFileCount,
                module.SymbolCount,
                module.DependencyModuleIds,
                module.Metadata
            }))));
        artifacts.AddRange(intelligence.Conventions.Select(convention =>
            KernelArtifact.Text(conventionDetails[convention.Id], KernelJson.Serialize(convention))));
        artifacts.AddRange(intelligence.Contracts.Select(contract =>
            KernelArtifact.Text(contractDetails[contract.Id], KernelJson.Serialize(contract))));
        artifacts.AddRange(resolvedEvidence.Select(evidence =>
            KernelArtifact.Text(evidenceDetails[evidence.Id], KernelJson.Serialize(evidence))));

        return artifacts.OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal).ToArray();
    }

    private static string RenderAgents(IntelligenceSnapshot intelligence, RetrievalPublication? retrieval)
    {
        var retrievalSteps = retrieval is null
            ? """
                No retrieval index was published, so start from the index files:

                1. Read `KERNEL.md` and `manifest.json`.
                2. Select the relevant entry from `modules/index.json`.
                3. Before changing a symbol, check `contracts/index.json`; a listed contract names the consumers that break.
                """
            : string.Create(CultureInfo.InvariantCulture, $"""
                Retrieval comes first. Reading the index files end to end costs more than asking:

                1. Query `{retrieval.RelativePath}` with the task in plain words, through `scout query` or any SQLite client.
                   It returns {retrieval.CardCount} cards over a {retrieval.TermCount}-term vocabulary, each naming a path and a detail file.
                2. Open only the detail files the query returned.
                3. Before changing a symbol, read the contract card it returns; a contract names the consumers that break.
                """);

        return $"""
            # Scout Kernel usage

            {retrievalSteps}
            4. Load only applicable conventions and evidence by reference.
            5. Treat `inference` as evidence-backed but fallible; treat missing knowledge as unknown.
            6. Inspect source only after the kernel narrows the likely scope.

            ## Roles

            `agents/index.json` holds role templates for this repository: what each role reads, verifies,
            and may hand off to. Data, not framework-bound prompts — each role reads only its own kernel
            projection, which is what keeps a multi-agent run cheaper than one agent reading everything.

            ## Skills

            **Orchestration skill** (`{OrchestrationSkillArtifacts.RelativeDirectory}/SKILL.md`): drives the
            role pipeline via a dependency-free Node script (`bin/scout-agents.mjs`), tracking turns and
            handoffs as JSON in `.internal/agent-sessions/`. Includes Codex/Copilot/Cursor adapters. Copy the
            folder to `.claude/skills/scout-agents/` to activate.

            **Query skill** (`{RetrievalQuerySkillArtifacts.RelativeDirectory}/SKILL.md`): direct queries
            against the retrieval index, no pipeline overhead. Copy to `.claude/skills/scout-query/` to activate.

            Concerns, flows, and canonical examples are not generated yet.
            """ + RenderDocumentationGuidance(intelligence);
    }

    private static string RenderDocumentationGuidance(IntelligenceSnapshot intelligence)
    {
        if (intelligence.DocumentedIntents.Count == 0)
        {
            return "\n";
        }

        return $"""


            ## Documentation

            `{DocumentationIndexPath}` lists {intelligence.DocumentedIntents.Count} documentation sections whose titles name a topic ({DocumentationCompiler.DescribeTopics(intelligence.DocumentedIntents)}).
            They are stated intent taken from the project's own documents, kept apart from observed conventions
            and contracts: only the titles were read, and documents can be out of date. Retrieve them with
            `--kind documentation`, open the section, and where it disagrees with an observed contract or
            convention, trust the observation and report the disagreement.

            Scout does not compare documents with code. Do it yourself, and only when the task depends on a
            document: if you would follow a section or cite it as fact, read its body, compare it with the code
            it describes, and report it as confirmed, a discrepancy (what it says, what the code shows, and where
            each is) or not checkable. A convention or concern the document states that no observed convention
            covers applies to this task as a documented, unverified rule; say so. Skip documents the task does
            not depend on.

            """;
    }

    private static string RenderKernel(
        RepositoryDescriptor repository,
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        RetrievalPublication? retrieval)
    {
        var revision = repository.Revision.Value ?? "Unknown";
        var entry = retrieval is null
            ? "Start with `manifest.json`. Then load only what applies:"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Start by querying `{retrieval.RelativePath}` ({retrieval.CardCount} cards). Load a file only when retrieval points at it:");

        return string.Create(CultureInfo.InvariantCulture, $"""
            # Scout Kernel

            Project: `{EscapeCode(repository.Name)}`  
            Source revision: `{EscapeCode(revision)}`  
            Source fingerprint: `{repository.ContentFingerprint}`

            {entry}

            - architecture: `architecture/overview.md`
            - report: `KERNEL_REPORT.md` (counts, most depended-on declarations, unknown areas; open on demand)
            - agent roles: `agents/index.json`
            - orchestration skill: `{OrchestrationSkillArtifacts.RelativeDirectory}/SKILL.md` (Architect/Developer/Reviewer/QA pipeline)
            - query skill: `{RetrievalQuerySkillArtifacts.RelativeDirectory}/SKILL.md` (fast direct lookups)
            - modules: `modules/index.json` ({intelligence.Modules.Count})
            - conventions: `conventions/index.json` ({intelligence.Conventions.Count})
            - contracts: `contracts/index.json` ({intelligence.Contracts.Count})
            - evidence: `evidence/index.json`{DocumentationEntry(intelligence)}
            - complete structural facts: `.internal/structural-ir.json`

            Unknown capabilities are explicit in the manifest. Structural counts: {structural.Projects.Count} projects, {structural.SourceFiles.Count} files, {DeclaredSymbolCount(structural)} symbols{TextLiteralClause(structural)}.
            """) + "\n";
    }

    // Wording is observed inside declarations, so counting it as a symbol would overstate what the project declares.
    private static int DeclaredSymbolCount(StructuralSnapshot structural) =>
        structural.Symbols.Count(symbol => symbol.SymbolKind != KnownSymbolKinds.TextLiteral);

    private static int TextLiteralCount(StructuralSnapshot structural) =>
        structural.Symbols.Count(symbol => symbol.SymbolKind == KnownSymbolKinds.TextLiteral);

    // Only said when there is wording to count, so a repository without any renders exactly as before.
    private static string TextLiteralClause(StructuralSnapshot structural)
    {
        var count = TextLiteralCount(structural);
        return count == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", {count} text literals");
    }

    private static string DocumentationEntry(IntelligenceSnapshot intelligence)
    {
        return intelligence.DocumentedIntents.Count == 0
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"\n- documentation: `{DocumentationIndexPath}` ({intelligence.DocumentedIntents.Count} documented sections, stated intent and not observed fact)");
    }

    private static string RenderDocumentationIndex(IntelligenceSnapshot intelligence)
    {
        var topicOrder = KnownDocumentationTopics.All
            .Select((topic, index) => (topic, index))
            .ToDictionary(item => item.topic, item => item.index, StringComparer.Ordinal);

        return KernelJson.Serialize(new
        {
            SchemaVersion = intelligence.SchemaVersion,
            Count = intelligence.DocumentedIntents.Count,
            Topics = DocumentationCompiler.DescribeTopics(intelligence.DocumentedIntents),
            Status = "Stated intent, not observed fact. Only section titles were read and the documents may be out of date. Open the section; where it disagrees with an observed contract or convention, trust the observation and report the disagreement.",
            Ordering = "Topic, then document path, then line.",
            Items = intelligence.DocumentedIntents
                .OrderBy(intent => topicOrder.GetValueOrDefault(intent.Topic, int.MaxValue))
                .ThenBy(intent => intent.RelativePath, StringComparer.Ordinal)
                .ThenBy(intent => intent.Line)
                .Select(intent => new
                {
                    intent.Id,
                    intent.Topic,
                    intent.Title,
                    Section = intent.SectionPath,
                    Path = intent.RelativePath,
                    intent.Line,
                    Classification = intent.Metadata.Classification,
                    Scope = intent.Metadata.Scope,
                    Confidence = intent.Metadata.Confidence
                })
                .ToArray()
        });
    }

    private static string RenderAgentIndex(
        IReadOnlyList<AgentTemplate> agents,
        Dictionary<string, string> details)
    {
        return KernelJson.Serialize(new
        {
            Count = agents.Count,
            Ordering = "The sequence roles are normally run in.",
            Status = "Role templates specialized against this repository. Data, not framework-bound prompts.",
            Items = agents.Select(agent => new
            {
                agent.Id,
                agent.Role,
                agent.Mandate,
                Handoffs = agent.Handoffs,
                Detail = details[agent.Id]
            }).ToArray()
        });
    }

    private static string RenderArchitecture(
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        Dictionary<StructuralId, string> moduleDetails)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Architecture overview")
            .AppendLine()
            .AppendLine("Status: basic structural projection. Project boundaries are exposed as inferred modules, not asserted as architectural truth.")
            .AppendLine()
            .Append("Observed: ")
            .Append(structural.Projects.Count.ToString(CultureInfo.InvariantCulture)).Append(" projects, ")
            .Append(structural.SourceFiles.Count.ToString(CultureInfo.InvariantCulture)).Append(" source files, ")
            .Append(DeclaredSymbolCount(structural).ToString(CultureInfo.InvariantCulture)).Append(" symbols, ")
            .Append(structural.Relationships.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" relationships.")
            .AppendLine()
            .AppendLine("Modules:");

        if (intelligence.Modules.Count == 0)
        {
            builder.AppendLine("- Unknown: no source project could be projected as a module.");
        }
        else
        {
            foreach (var module in intelligence.Modules)
            {
                builder.Append("- `").Append(EscapeCode(module.Name)).Append("`: ")
                    .Append(module.Summary).Append(" See `")
                    .Append(moduleDetails[module.Id]).AppendLine("`.");
            }
        }

        builder.AppendLine()
            .Append("Analysis diagnostics: ")
            .Append(structural.Diagnostics.Count.ToString(CultureInfo.InvariantCulture))
            .AppendLine(". See `.internal/structural-ir.json` for details.");
        AppendDocumentedIntent(builder, intelligence);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void AppendDocumentedIntent(StringBuilder builder, IntelligenceSnapshot intelligence)
    {
        if (intelligence.DocumentedIntents.Count == 0)
        {
            return;
        }

        builder.AppendLine()
            .AppendLine("Documented intent (stated by the project's documents, not observed in code; only titles were read):");
        foreach (var topic in KnownDocumentationTopics.All)
        {
            var sections = intelligence.DocumentedIntents
                .Where(intent => intent.Topic == topic)
                .OrderBy(intent => intent.RelativePath, StringComparer.Ordinal)
                .ThenBy(intent => intent.Line)
                .ToArray();
            if (sections.Length == 0)
            {
                continue;
            }

            builder.Append("- ").Append(topic).Append(": ")
                .Append(sections.Length.ToString(CultureInfo.InvariantCulture)).Append(" sections, e.g. `")
                .Append(EscapeCode(sections[0].SectionPath)).AppendLine("`.");
        }

        builder.Append("See `").Append(DocumentationIndexPath).AppendLine("`.");
    }

    private static string RenderManifest(
        RepositoryDescriptor repository,
        StructuralSnapshot structural,
        IntelligenceSnapshot intelligence,
        string scoutVersion,
        int unresolvedEvidenceCount,
        RetrievalPublication? retrieval)
    {
        return KernelJson.Serialize(new
        {
            Format = "scout-kernel",
            KernelVersion,
            StructuralSchemaVersion = structural.SchemaVersion,
            IntelligenceSchemaVersion = intelligence.SchemaVersion,
            ScoutVersion = scoutVersion,
            GeneratedAtUtc = structural.CreatedAtUtc,
            Source = new
            {
                repository.Name,
                RevisionKind = repository.Revision.Kind,
                Revision = repository.Revision.Value,
                repository.Revision.IsDirty,
                ContentFingerprint = repository.ContentFingerprint
            },
            Analyzers = structural.Analyzers.Select(analyzer => new
            {
                analyzer.AnalyzerId,
                analyzer.Name,
                analyzer.Version,
                analyzer.LanguageId,
                analyzer.Status
            }).ToArray(),
            Entrypoints = new
            {
                Kernel = "KERNEL.md",
                AgentInstructions = "AGENTS.md",
                AgentRoles = "agents/index.json",
                AgentOrchestrationSkill = $"{OrchestrationSkillArtifacts.RelativeDirectory}/SKILL.md",
                Architecture = "architecture/overview.md",
                Modules = "modules/index.json",
                Conventions = "conventions/index.json",
                Contracts = "contracts/index.json",
                Evidence = "evidence/index.json",
                Documentation = intelligence.DocumentedIntents.Count == 0 ? null : DocumentationIndexPath,
                Retrieval = retrieval?.RelativePath,
                StructuralIr = ".internal/structural-ir.json"
            },
            Refresh = new
            {
                Strategy = "atomic-in-place",
                SupportsPipelines = true,
                ReplacesExistingKernel = true,
                PreservesPreviousKernelDuringSwap = true,
                SafeToRunRepeatedly = true
            },
            Retrieval = retrieval is null
                ? null
                : new
                {
                    Path = retrieval.RelativePath,
                    retrieval.Format,
                    retrieval.ModelId,
                    retrieval.CardCount,
                    retrieval.TermCount,
                    retrieval.DenseModelId
                },
            Capabilities = new
            {
                Architecture = "basic",
                Modules = "basic-project-projection",
                Conventions = intelligence.Conventions.Count == 0 ? "unknown" : "basic-scoped-naming-and-structure",
                Skills = "vendor-agnostic-role-orchestration-skill",
                Concerns = "unknown",
                Documentation = intelligence.DocumentedIntents.Count == 0 ? null : "heading-vocabulary-stated-intent",
                Contracts = intelligence.Contracts.Count == 0 ? "unknown" : "observed-consumption-fan-in",
                Flows = "unknown",
                CanonicalExamples = "unknown",
                AgentRoles = "specialized-role-templates",
                Retrieval = retrieval is null ? "unknown" : "lexical-embedding-index",
                Routing = "unknown"
            },
            UnknownAreas = intelligence.UnknownAreas,
            Diagnostics = new
            {
                AnalysisCount = structural.Diagnostics.Count,
                UnresolvedEvidenceCount = unresolvedEvidenceCount
            }
        });
    }

    private static string RenderModuleIndex(
        IntelligenceSnapshot intelligence,
        Dictionary<StructuralId, string> details)
    {
        return KernelJson.Serialize(new
        {
            SchemaVersion = intelligence.SchemaVersion,
            Count = intelligence.Modules.Count,
            Items = intelligence.Modules.Select(module => new
            {
                module.Id,
                module.Name,
                module.Summary,
                Scope = module.Metadata.Scope,
                Confidence = module.Metadata.Confidence,
                Detail = details[module.Id]
            }).ToArray()
        });
    }

    private static string RenderConventionIndex(
        IntelligenceSnapshot intelligence,
        Dictionary<StructuralId, string> details)
    {
        return KernelJson.Serialize(new
        {
            SchemaVersion = intelligence.SchemaVersion,
            Count = intelligence.Conventions.Count,
            Status = intelligence.Conventions.Count == 0
                ? "Unknown: no convention met the minimum support and adherence thresholds."
                : "Evidence-backed scoped inferences; load details only when their scope applies.",
            Items = intelligence.Conventions.Select(convention => new
            {
                convention.Id,
                convention.Rule,
                Scope = convention.Metadata.Scope,
                Confidence = convention.Metadata.Confidence,
                Detail = details[convention.Id]
            }).ToArray()
        });
    }

    private static string RenderContractIndex(
        IntelligenceSnapshot intelligence,
        Dictionary<StructuralId, string> details)
    {
        return KernelJson.Serialize(new
        {
            SchemaVersion = intelligence.SchemaVersion,
            Count = intelligence.Contracts.Count,
            Ordering = "Descending blast radius, then identifier.",
            Status = intelligence.Contracts.Count == 0
                ? "Unknown: no symbol had enough observed consumers to be treated as a stability boundary."
                : "Evidence-backed boundaries; read a detail before changing the shape of its symbol.",
            Items = intelligence.Contracts
                .OrderByDescending(contract => contract.BlastRadius)
                .ThenBy(contract => contract.Id)
                .Select(contract => new
                {
                    contract.Id,
                    contract.Name,
                    contract.ContractKind,
                    contract.Visibility,
                    contract.RelativePath,
                    contract.BlastRadius,
                    ConsumerModuleCount = contract.ConsumerModuleIds.Count,
                    Scope = contract.Metadata.Scope,
                    Confidence = contract.Metadata.Confidence,
                    Detail = details[contract.Id]
                })
                .ToArray()
        });
    }

    private static string RenderEvidenceIndex(
        EvidenceFact[] evidence,
        Dictionary<StructuralId, string> details,
        StructuralId[] unresolvedEvidence)
    {
        return KernelJson.Serialize(new
        {
            Count = evidence.Length,
            Items = evidence.Select(item => new
            {
                item.Id,
                item.Kind,
                Summary = item.Description,
                Detail = details[item.Id]
            }).ToArray(),
            UnresolvedEvidenceIds = unresolvedEvidence
        });
    }

    private static string ToFileStem(StructuralId id)
    {
        var builder = new StringBuilder(id.Value.Length);
        foreach (var character in id.Value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-');
        }

        return builder.ToString();
    }

    private static string EscapeCode(string value) => value.Replace('`', '\'');
}

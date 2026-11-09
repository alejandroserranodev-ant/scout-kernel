using System.Diagnostics;
using System.Text.Json;
using Scout.Analysis.CSharp;
using Scout.Analysis.Markdown;
using Scout.Core.Agents;
using Scout.Core.Analysis;
using Scout.Core.Compilation;
using Scout.Core.Intelligence;
using Scout.Core.Retrieval;
using Scout.Retrieval.Sqlite;
using Scout.Tests.Support;

namespace Scout.Tests.Intelligence;

public sealed class DocumentationIntelligenceTests
{
    private const string CSharpFixture = "MinimalCSharpRepository";

    [Fact]
    public async Task CompileReportsSectionsTitledForATopicAsStatedIntent()
    {
        using var repository = CreateRepositoryWithDocumentation();

        var result = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);

        var topicsByTitle = result.Intelligence.DocumentedIntents
            .GroupBy(intent => intent.Title)
            .ToDictionary(
                group => group.Key,
                group => group.Select(intent => intent.Topic).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal(["architecture"], topicsByTitle["Arquitectura"]);
        Assert.Equal(["conventions"], topicsByTitle["Convenciones de código"]);
        Assert.Equal(["flows"], topicsByTitle["Flujo de pagos"]);
        Assert.Equal(["concerns"], topicsByTitle["Seguridad y privacidad"]);
        Assert.Equal(["operations"], topicsByTitle["Configuración del entorno"]);
        Assert.Equal(["testing"], topicsByTitle["Pruebas"]);
        Assert.Equal(["architecture", "testing"], topicsByTitle["Diseño y pruebas"]);
        Assert.DoesNotContain("Historia", topicsByTitle.Keys);
        Assert.DoesNotContain("Billing", topicsByTitle.Keys);

        Assert.All(result.Intelligence.DocumentedIntents, intent =>
        {
            Assert.Equal(KnowledgeClassification.Recommendation, intent.Metadata.Classification);
            Assert.Equal(0.5, intent.Metadata.Confidence.Value);
            Assert.NotEmpty(intent.Metadata.EvidenceIds);
        });
    }

    [Fact]
    public async Task CompileScopesADocumentToTheCodeAroundItsDocumentationFolder()
    {
        using var repository = CreateRepositoryWithDocumentation();

        var result = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);

        var rootDocument = result.Intelligence.DocumentedIntents.First(intent => intent.RelativePath == "docs/guia.md");
        Assert.Equal("**", rootDocument.Metadata.Scope);

        var nestedDocument = result.Intelligence.DocumentedIntents.First(intent => intent.RelativePath == "apps/web/docs/api.md");
        Assert.Equal("apps/web/**", nestedDocument.Metadata.Scope);
    }

    [Fact]
    public async Task CompileKeepsDocumentedIntentApartFromObservedClaims()
    {
        using var repository = CreateRepositoryWithDocumentation();

        var withDocumentation = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);
        var withoutDocumentation = await CompileAsync(repository, withDocumentation: false).ConfigureAwait(true);

        Assert.Equal(
            withoutDocumentation.Intelligence.Conventions.Select(convention => convention.Id),
            withDocumentation.Intelligence.Conventions.Select(convention => convention.Id));
        Assert.Equal(
            withoutDocumentation.Intelligence.Contracts.Select(contract => contract.Id),
            withDocumentation.Intelligence.Contracts.Select(contract => contract.Id));
        Assert.Equal(
            withoutDocumentation.Intelligence.UnknownAreas,
            withDocumentation.Intelligence.UnknownAreas);
    }

    [Fact]
    public async Task CompilePublishesTheDocumentationIndexOnlyWhenThereIsDocumentedIntent()
    {
        using var repository = CreateRepositoryWithDocumentation();

        var documented = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);
        var plain = await CompileAsync(repository, withDocumentation: false).ConfigureAwait(true);

        using var index = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(documented.Kernel.OutputPath, "documentation", "index.json")));
        Assert.Equal(documented.Intelligence.DocumentedIntents.Count, index.RootElement.GetProperty("count").GetInt32());
        Assert.Contains("not observed fact", index.RootElement.GetProperty("status").GetString(), StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(documented.Kernel.OutputPath, "manifest.json")));
        Assert.Equal(
            "documentation/index.json",
            manifest.RootElement.GetProperty("entrypoints").GetProperty("documentation").GetString());

        var kernelText = await File.ReadAllTextAsync(Path.Combine(documented.Kernel.OutputPath, "KERNEL.md"));
        Assert.Contains("documentation/index.json", kernelText, StringComparison.Ordinal);
        var agentsText = await File.ReadAllTextAsync(Path.Combine(documented.Kernel.OutputPath, "AGENTS.md"));
        Assert.Contains("## Documentation", agentsText, StringComparison.Ordinal);
        Assert.Contains("--kind documentation", agentsText, StringComparison.Ordinal);
        Assert.Contains("only when the task depends on a", agentsText, StringComparison.Ordinal);

        Assert.Empty(plain.Intelligence.DocumentedIntents);
        Assert.False(File.Exists(Path.Combine(plain.Kernel.OutputPath, "documentation", "index.json")));
        using var plainManifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(plain.Kernel.OutputPath, "manifest.json")));
        Assert.False(plainManifest.RootElement.GetProperty("entrypoints").TryGetProperty("documentation", out _));
        Assert.DoesNotContain(
            "## Documentation",
            await File.ReadAllTextAsync(Path.Combine(plain.Kernel.OutputPath, "AGENTS.md")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetrievalFindsDocumentationSectionsRegardlessOfAccentsAndKeepsThemOutOfSymbols()
    {
        using var repository = CreateRepositoryWithDocumentation();
        var result = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);

        using var database = SqliteRetrievalDatabase.Open(
            Path.Combine(result.Kernel.OutputPath, "retrieval", "index.db"),
            new LexicalEmbeddingModel());

        var hit = Assert.Single(database.Search("configuracion del entorno", limit: 1, kind: KnownCardKinds.Documentation));
        Assert.Equal("Configuración del entorno", hit.Card.Name);
        Assert.Equal(KnownCardKinds.Documentation, hit.Card.Kind);
        Assert.Contains(hit.Card.Facets, facet => facet is { Name: "topic", Value: "operations" });
        Assert.Equal("documentation/index.json", hit.Card.Detail);

        var plainSection = Assert.Single(database.Search("Historia", limit: 1, kind: KnownCardKinds.Documentation));
        Assert.DoesNotContain(plainSection.Card.Facets, facet => facet.Name == "topic");
        Assert.Null(plainSection.Card.Detail);

        Assert.DoesNotContain(
            database.Search("Arquitectura", limit: 20, kind: KnownCardKinds.Symbol),
            candidate => candidate.Card.Name == "Arquitectura");
    }

    [Fact]
    public async Task AgentsAreOnlyPointedAtDocumentationTheKernelHas()
    {
        using var repository = CreateRepositoryWithDocumentation();
        var documented = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);
        var plain = await CompileAsync(repository, withDocumentation: false).ConfigureAwait(true);

        var withDocs = AgentTemplateCompiler.Compile(documented.Structural, documented.Intelligence, "retrieval/index.db");
        var withoutDocs = AgentTemplateCompiler.Compile(plain.Structural, plain.Intelligence, "retrieval/index.db");

        foreach (var role in withDocs)
        {
            Assert.Contains("documentation/index.json", role.Reads);
            Assert.Contains(KnownCardKinds.Documentation, role.RetrievalKinds);
            Assert.Contains(role.Obligations, obligation => obligation.Contains("document", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(role.AutoQueries, query => query.Kind == KnownCardKinds.Documentation);
            Assert.Contains(role.Obligations, obligation => obligation.StartsWith("Verify documentation only when the task depends on it", StringComparison.Ordinal));
        }

        var architect = withDocs.Single(role => role.Id == "architect");
        Assert.Contains(architect.Obligations, obligation => obligation.Contains("stated intent", StringComparison.Ordinal));
        var reviewer = withDocs.Single(role => role.Id == "reviewer");
        Assert.Contains(reviewer.Obligations, obligation => obligation.Contains("advisory", StringComparison.Ordinal));

        foreach (var role in withoutDocs)
        {
            Assert.DoesNotContain("documentation/index.json", role.Reads);
            Assert.DoesNotContain(KnownCardKinds.Documentation, role.RetrievalKinds);
            Assert.DoesNotContain(role.AutoQueries, query => query.Kind == KnownCardKinds.Documentation);
            Assert.DoesNotContain(role.Obligations, obligation => obligation.StartsWith("Verify documentation", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ArchitectSaysDocumentationDoesNotMakeAnUnknownAreaKnown()
    {
        using var repository = CreateRepositoryWithDocumentation();
        var result = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);

        var architect = AgentTemplateCompiler
            .Compile(result.Structural, result.Intelligence, "retrieval/index.db")
            .Single(role => role.Id == "architect");

        // The fixture documents 'flows' and 'concerns', two areas the kernel still lists as unknown.
        Assert.Contains(
            architect.Obligations,
            obligation => obligation.Contains("does not make the area known", StringComparison.Ordinal)
                && obligation.Contains("concerns", StringComparison.Ordinal)
                && obligation.Contains("flows", StringComparison.Ordinal));
        Assert.Contains("concerns", result.Intelligence.UnknownAreas);
        Assert.Contains("flows", result.Intelligence.UnknownAreas);
    }

    [Fact]
    public async Task PublishedSkillsRunAgainstTheKernelTheyShipIn()
    {
        using var repository = CreateRepositoryWithDocumentation();
        var result = await CompileAsync(repository, withDocumentation: true).ConfigureAwait(true);
        var kernel = result.Kernel.OutputPath;

        Assert.True(File.Exists(Path.Combine(kernel, "skills", "scout-query", "bin", "lib", "retrieval-engine.mjs")));
        Assert.True(File.Exists(Path.Combine(kernel, "skills", "scout-agents", "bin", "lib", "retrieval-engine.mjs")));
        Assert.True(File.Exists(Path.Combine(kernel, "skills", "scout-agents", "bin", "lib", "agent-retrieval-executor.mjs")));

        var query = await RunNodeAsync(
            Path.Combine(kernel, "skills", "scout-query", "bin", "scout-query.mjs"),
            "configuracion entorno",
            "--kind",
            "documentation",
            "--kernel",
            kernel,
            "--json").ConfigureAwait(true);
        Assert.Equal(0, query.ExitCode);
        Assert.Contains("Configuración del entorno", query.Output, StringComparison.Ordinal);

        var start = await RunNodeAsync(
            Path.Combine(kernel, "skills", "scout-agents", "bin", "scout-agents.mjs"),
            "start",
            "--kernel",
            kernel,
            "--task",
            "revisar la seguridad").ConfigureAwait(true);
        Assert.Equal(0, start.ExitCode);
        Assert.Contains("documentation", start.Output, StringComparison.Ordinal);
    }

    private static TemporaryRepository CreateRepositoryWithDocumentation()
    {
        var repository = TemporaryRepository.CopyFixture(CSharpFixture, "documented source");
        repository.WriteFile(
            "docs/guia.md",
            """
            # Arquitectura

            ## Convenciones de código

            ## Flujo de pagos

            ## Seguridad y privacidad

            ## Configuración del entorno

            ## Pruebas

            ## Diseño y pruebas

            ## Historia
            """);
        repository.WriteFile("apps/web/docs/api.md", "# API de facturación\n");
        repository.WriteFile("README.md", "# Billing\n");
        return repository;
    }

    private static async Task<ScoutCompilationResult> CompileAsync(TemporaryRepository repository, bool withDocumentation)
    {
        ILanguageAnalyzer[] analyzers = withDocumentation
            ? [new CSharpLanguageAnalyzer(), new MarkdownLanguageAnalyzer()]
            : [new CSharpLanguageAnalyzer()];
        var output = Path.Combine(
            repository.WorkspacePath,
            withDocumentation ? "documented.scout" : "plain.scout");
        return await new ScoutCompiler(analyzers, indexWriter: new SqliteRetrievalIndexWriter())
            .CompileAsync(new ScoutCompileRequest(repository.RootPath, output))
            .ConfigureAwait(false);
    }

    private static async Task<(int ExitCode, string Output)> RunNodeAsync(string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
    }
}

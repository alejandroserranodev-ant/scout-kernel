using System.Collections.ObjectModel;

namespace Scout.Core.Intelligence;

/// <summary>
/// Contains project intelligence derived from, but intentionally separate from, structural IR facts.
/// </summary>
public sealed class IntelligenceSnapshot
{
    /// <summary>Gets the current intelligence schema version.</summary>
    public const string CurrentSchemaVersion = "1.2";

    /// <summary>
    /// Initializes a deterministic intelligence snapshot.
    /// </summary>
    /// <param name="createdAtUtc">The UTC creation timestamp.</param>
    /// <param name="modules">The inferred module catalog.</param>
    /// <param name="conventions">The inferred scoped convention catalog.</param>
    /// <param name="contracts">The inferred contract catalog.</param>
    /// <param name="unknownAreas">Capabilities not established by this compilation.</param>
    /// <param name="schemaVersion">The persisted intelligence schema version.</param>
    /// <param name="documentedIntents">The documentation sections whose titles name a topic.</param>
    public IntelligenceSnapshot(
        DateTimeOffset createdAtUtc,
        IEnumerable<ModuleIntelligence>? modules = null,
        IEnumerable<ConventionIntelligence>? conventions = null,
        IEnumerable<ContractIntelligence>? contracts = null,
        IEnumerable<string>? unknownAreas = null,
        string schemaVersion = CurrentSchemaVersion,
        IEnumerable<DocumentedIntent>? documentedIntents = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);

        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        SchemaVersion = schemaVersion.Trim();
        Modules = new ReadOnlyCollection<ModuleIntelligence>(
            modules?.OrderBy(module => module.Id).ToArray() ?? []);
        Conventions = new ReadOnlyCollection<ConventionIntelligence>(
            conventions?.OrderBy(convention => convention.Id).ToArray() ?? []);
        Contracts = new ReadOnlyCollection<ContractIntelligence>(
            contracts?.OrderBy(contract => contract.Id).ToArray() ?? []);
        DocumentedIntents = new ReadOnlyCollection<DocumentedIntent>(
            documentedIntents?.OrderBy(intent => intent.Id).ToArray() ?? []);
        UnknownAreas = new ReadOnlyCollection<string>(
            unknownAreas?
                .Where(area => !string.IsNullOrWhiteSpace(area))
                .Select(area => area.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray() ?? []);
    }

    /// <summary>Gets the persisted intelligence schema version.</summary>
    public string SchemaVersion { get; }

    /// <summary>Gets the UTC creation timestamp.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the sorted inferred module catalog.</summary>
    public IReadOnlyList<ModuleIntelligence> Modules { get; }

    /// <summary>Gets the sorted inferred convention catalog.</summary>
    public IReadOnlyList<ConventionIntelligence> Conventions { get; }

    /// <summary>Gets the sorted inferred contract catalog.</summary>
    public IReadOnlyList<ContractIntelligence> Contracts { get; }

    /// <summary>
    /// Gets the sorted documentation sections whose titles name a topic. These are stated intent taken from the
    /// project's documentation, kept apart from the observed claims above.
    /// </summary>
    public IReadOnlyList<DocumentedIntent> DocumentedIntents { get; }

    /// <summary>Gets the areas this intelligence snapshot explicitly does not establish.</summary>
    public IReadOnlyList<string> UnknownAreas { get; }
}

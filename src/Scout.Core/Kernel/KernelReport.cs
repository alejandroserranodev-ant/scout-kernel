using System.Globalization;
using System.Text;
using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Repository;

namespace Scout.Core.Kernel;

/// <summary>
/// Renders <c>KERNEL_REPORT.md</c>: a short, deterministic orientation a reader opens on demand, never at the
/// start of a session. It states only counts and rankings computed from observed facts.
/// </summary>
internal static class KernelReport
{
    internal const string RelativePath = "KERNEL_REPORT.md";
    internal const int MaxLines = 100;

    private const int MaxLanguages = 8;
    private const int MaxModules = 10;
    private const int MaxHubs = 10;
    private const int MaxUnknownAreas = 10;

    // Relationships that mean "this declaration depends on that one"; a hub is whatever many others depend on.
    private static readonly string[] DependencyKinds =
    [
        KnownRelationshipKinds.Calls,
        KnownRelationshipKinds.References,
        KnownRelationshipKinds.Renders,
        KnownRelationshipKinds.Inherits,
        KnownRelationshipKinds.Implements
    ];

    internal static string Render(RepositoryDescriptor repository, StructuralSnapshot structural, IntelligenceSnapshot intelligence)
    {
        var lines = new List<string>
        {
            "# Kernel report",
            string.Empty,
            "Orientation for a human or an agent that wants the shape of the project. Counts and rankings only, computed from",
            "observed facts; open it on demand, not at the start of a session.",
            string.Empty,
            "## Size",
            string.Empty,
            Line($"- {structural.Projects.Count} projects, {structural.SourceFiles.Count} source files, {structural.Symbols.Count(IsDeclared)} declared symbols"),
            Line($"- {structural.Relationships.Count} relationships: {string.Join(", ", RelationshipCounts(structural))}"),
            string.Empty,
            "## Files by language",
            string.Empty
        };

        foreach (var group in structural.SourceFiles
            .GroupBy(file => file.LanguageId, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Take(MaxLanguages))
        {
            lines.Add(Line($"- {group.Key}: {group.Count()}"));
        }

        lines.Add(string.Empty);
        lines.Add("## Largest modules");
        lines.Add(string.Empty);
        foreach (var module in intelligence.Modules
            .OrderByDescending(module => module.SymbolCount)
            .ThenBy(module => module.Name, StringComparer.Ordinal)
            .Take(MaxModules))
        {
            lines.Add(Line($"- {module.Name} ({module.RelativePath}): {module.SourceFileCount} files, {module.SymbolCount} symbols"));
        }

        lines.Add(string.Empty);
        lines.Add("## Most depended-on declarations");
        lines.Add(string.Empty);
        foreach (var hub in Hubs(structural))
        {
            lines.Add(Line($"- {hub.Name} ({hub.Location}): {hub.Dependents} dependents"));
        }

        lines.Add(string.Empty);
        lines.Add("## Unknown areas");
        lines.Add(string.Empty);
        if (intelligence.UnknownAreas.Count == 0)
        {
            lines.Add("- none recorded");
        }
        else
        {
            lines.AddRange(intelligence.UnknownAreas.Take(MaxUnknownAreas).Select(area => Line($"- {area}")));
            if (intelligence.UnknownAreas.Count > MaxUnknownAreas)
            {
                lines.Add(Line($"- ... and {intelligence.UnknownAreas.Count - MaxUnknownAreas} more"));
            }
        }

        lines.Add(Line($"- analysis diagnostics: {structural.Diagnostics.Count}"));
        lines.Add(string.Empty);
        lines.Add("## Freshness");
        lines.Add(string.Empty);
        lines.Add(Line($"- source revision: {repository.Revision.Value ?? "unknown"}"));
        lines.Add(Line($"- source fingerprint: {repository.ContentFingerprint}"));

        if (lines.Count > MaxLines)
        {
            lines = [.. lines.Take(MaxLines - 1), "... truncated to the line cap."];
        }

        return string.Join("\n", lines) + "\n";
    }

    private static bool IsDeclared(SymbolFact symbol) => symbol.SymbolKind != KnownSymbolKinds.TextLiteral;

    private static string Line(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> RelationshipCounts(StructuralSnapshot structural) =>
        structural.Relationships
            .GroupBy(relationship => relationship.Kind, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key} {group.Count()}"));

    private static IEnumerable<(string Name, string Location, int Dependents)> Hubs(StructuralSnapshot structural)
    {
        var symbolsById = structural.Symbols.ToDictionary(symbol => symbol.Id);
        return structural.Relationships
            .Where(relationship => DependencyKinds.Contains(relationship.Kind, StringComparer.Ordinal)
                && relationship.SourceId != relationship.TargetId
                && symbolsById.TryGetValue(relationship.TargetId, out var target)
                && IsDeclared(target)
                && target.SymbolKind != KnownSymbolKinds.DocumentSection
                && target.DeclarationLocations.Count > 0)
            .GroupBy(relationship => relationship.TargetId)
            .Select(group => (Target: symbolsById[group.Key], Dependents: group.Select(r => r.SourceId).Distinct().Count()))
            .OrderByDescending(entry => entry.Dependents)
            .ThenBy(entry => entry.Target.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Target.DeclarationLocations[0].RelativePath, StringComparer.Ordinal)
            .Take(MaxHubs)
            .Select(entry => (
                entry.Target.Name,
                $"{entry.Target.DeclarationLocations[0].RelativePath}:{entry.Target.DeclarationLocations[0].StartLine.ToString(CultureInfo.InvariantCulture)}",
                entry.Dependents));
    }
}

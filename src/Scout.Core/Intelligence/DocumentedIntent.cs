using Scout.Core.Ir;

namespace Scout.Core.Intelligence;

/// <summary>
/// A documentation section whose title names a topic, reported as stated intent rather than observed fact.
/// </summary>
/// <remarks>
/// Observed conventions, contracts and modules are derived from code and carry the evidence of what was seen.
/// A documented intent is different in kind: it says where the project's own documentation speaks about a
/// topic. It is never merged with observed claims, because documentation can be out of date and an agent
/// must be able to tell what was written from what was seen.
/// </remarks>
public sealed class DocumentedIntent
{
    /// <summary>
    /// Initializes a documented intent.
    /// </summary>
    /// <param name="id">The stable identifier of the intent.</param>
    /// <param name="topic">The topic the section title names.</param>
    /// <param name="sectionSymbolId">The structural identifier of the documentation section.</param>
    /// <param name="title">The section title.</param>
    /// <param name="sectionPath">The qualified path of the section, including the document it is in.</param>
    /// <param name="relativePath">The repository-relative path of the document.</param>
    /// <param name="line">The one-based line of the section heading.</param>
    /// <param name="summary">A statement of what was established and what was not.</param>
    /// <param name="metadata">The classification, confidence, scope and evidence of the claim.</param>
    public DocumentedIntent(
        StructuralId id,
        string topic,
        StructuralId sectionSymbolId,
        string title,
        string sectionPath,
        string relativePath,
        int line,
        string summary,
        KnowledgeMetadata metadata)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A documented intent identifier cannot be empty.", nameof(id));
        }

        if (sectionSymbolId.IsEmpty)
        {
            throw new ArgumentException("A documented intent must name its section.", nameof(sectionSymbolId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);

        Id = id;
        Topic = topic.Trim();
        SectionSymbolId = sectionSymbolId;
        Title = title.Trim();
        SectionPath = sectionPath.Trim();
        RelativePath = Ir.RepositoryPath.Normalize(relativePath);
        Line = line;
        Summary = summary.Trim();
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Gets the stable identifier of the intent.
    /// </summary>
    public StructuralId Id { get; }

    /// <summary>
    /// Gets the topic the section title names.
    /// </summary>
    public string Topic { get; }

    /// <summary>
    /// Gets the structural identifier of the documentation section.
    /// </summary>
    public StructuralId SectionSymbolId { get; }

    /// <summary>
    /// Gets the section title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the qualified path of the section, including the document it is in.
    /// </summary>
    public string SectionPath { get; }

    /// <summary>
    /// Gets the repository-relative path of the document.
    /// </summary>
    public string RelativePath { get; }

    /// <summary>
    /// Gets the one-based line of the section heading.
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets a statement of what was established and what was not.
    /// </summary>
    public string Summary { get; }

    /// <summary>
    /// Gets the classification, confidence, scope and evidence of the claim.
    /// </summary>
    public KnowledgeMetadata Metadata { get; }
}

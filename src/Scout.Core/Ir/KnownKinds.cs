namespace Scout.Core.Ir;

/// <summary>
/// Provides well-known structural entity kinds while allowing analyzers to emit additional string kinds.
/// </summary>
public static class KnownEntityKinds
{
    /// <summary>Identifies a source project entity.</summary>
    public const string Project = "project";

    /// <summary>Identifies a repository source file entity.</summary>
    public const string SourceFile = "source-file";

    /// <summary>Identifies a source language symbol entity.</summary>
    public const string Symbol = "symbol";
}

/// <summary>
/// Provides well-known language-neutral symbol kinds while allowing analyzers to emit additional string kinds.
/// </summary>
public static class KnownSymbolKinds
{
    /// <summary>Identifies a namespace-like declaration.</summary>
    public const string Namespace = "namespace";

    /// <summary>Identifies a named type declaration.</summary>
    public const string Type = "type";

    /// <summary>Identifies a callable declaration.</summary>
    public const string Callable = "callable";

    /// <summary>Identifies a stored or computed value declaration.</summary>
    public const string Value = "value";

    /// <summary>
    /// Identifies a titled section of a document, such as a Markdown heading. Sections describe a project
    /// instead of implementing it, so they are retrieved as documentation and never as code.
    /// </summary>
    public const string DocumentSection = "document-section";

    /// <summary>
    /// Identifies wording a source declaration carries, such as the text of a button or an error message. A text
    /// literal is observed in a declaration instead of being declared, so it is never a navigation target or a
    /// contract: it is retrieved only to point at the declaration that holds it.
    /// </summary>
    public const string TextLiteral = "text-literal";

    /// <summary>Identifies a symbol without a known language-neutral classification.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// Provides well-known language-neutral symbol visibilities while allowing analyzers to emit additional string kinds.
/// </summary>
/// <remarks>
/// Visibility is an observed declaration fact. It never asserts that a symbol is an intentional contract;
/// that remains a separate, evidence-backed intelligence claim.
/// </remarks>
public static class KnownVisibilities
{
    /// <summary>Identifies a declaration visible to every consumer.</summary>
    public const string Public = "public";

    /// <summary>Identifies a declaration visible only to derived declarations.</summary>
    public const string Protected = "protected";

    /// <summary>Identifies a declaration visible only within its compilation boundary.</summary>
    public const string Internal = "internal";

    /// <summary>Identifies a declaration visible only within its declaring scope.</summary>
    public const string Private = "private";

    /// <summary>Identifies a declaration whose visibility the analyzer did not establish.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// Provides well-known relationship kinds while allowing analyzers to emit additional string kinds.
/// </summary>
public static class KnownRelationshipKinds
{
    /// <summary>Indicates structural containment.</summary>
    public const string Contains = "contains";

    /// <summary>Indicates that an entity defines another entity.</summary>
    public const string Defines = "defines";

    /// <summary>Indicates a dependency between source projects.</summary>
    public const string ProjectDependsOn = "project-depends-on";

    /// <summary>Indicates inheritance from another symbol.</summary>
    public const string Inherits = "inherits";

    /// <summary>Indicates implementation of another symbol contract.</summary>
    public const string Implements = "implements";

    /// <summary>Indicates a semantic reference to another entity.</summary>
    public const string References = "references";

    /// <summary>Indicates a call from one callable to another callable.</summary>
    public const string Calls = "calls";

    /// <summary>Indicates that a view symbol renders another view symbol, such as a React component in JSX.</summary>
    public const string Renders = "renders";
}

/// <summary>
/// Provides well-known evidence kinds while allowing analyzers to emit additional string kinds.
/// </summary>
public static class KnownEvidenceKinds
{
    /// <summary>Identifies evidence originating in repository metadata.</summary>
    public const string RepositoryMetadata = "repository-metadata";

    /// <summary>Identifies evidence originating in project metadata.</summary>
    public const string ProjectMetadata = "project-metadata";

    /// <summary>Identifies evidence originating in a source declaration.</summary>
    public const string SourceDeclaration = "source-declaration";

    /// <summary>Identifies evidence established by semantic binding.</summary>
    public const string SemanticBinding = "semantic-binding";
}

/// <summary>
/// Provides well-known diagnostic severities while allowing analyzers to emit additional string severities.
/// </summary>
public static class KnownDiagnosticSeverities
{
    /// <summary>Identifies informational diagnostic output.</summary>
    public const string Information = "information";

    /// <summary>Identifies a diagnostic that may reduce analysis completeness.</summary>
    public const string Warning = "warning";

    /// <summary>Identifies a diagnostic that prevented part of the analysis.</summary>
    public const string Error = "error";
}

/// <summary>
/// Provides well-known source revision kinds while allowing repository discovery to emit additional kinds.
/// </summary>
public static class KnownRevisionKinds
{
    /// <summary>Identifies a Git commit hash.</summary>
    public const string GitCommit = "git-commit";

    /// <summary>Identifies a source revision that could not be determined.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// Provides well-known analyzer run statuses while allowing analyzers to emit additional statuses.
/// </summary>
public static class KnownAnalyzerStatuses
{
    /// <summary>Indicates that the analyzer completed its declared scope.</summary>
    public const string Succeeded = "succeeded";

    /// <summary>Indicates that the analyzer produced usable but incomplete facts.</summary>
    public const string Partial = "partial";

    /// <summary>Indicates that the analyzer could not produce its expected facts.</summary>
    public const string Failed = "failed";
}

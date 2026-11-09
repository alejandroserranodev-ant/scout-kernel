using System.Text.Json.Serialization;

namespace Scout.Analysis.TypeScript;

/// <summary>
/// Describes the request sent to the TypeScript sidecar.
/// </summary>
internal sealed class SidecarRequest
{
    [JsonPropertyName("rootPath")]
    public required string RootPath { get; init; }

    [JsonPropertyName("files")]
    public required IReadOnlyList<string> Files { get; init; }
}

/// <summary>
/// Describes the structural payload returned by the TypeScript sidecar.
/// </summary>
internal sealed class SidecarResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "failed";

    [JsonPropertyName("typeScriptVersion")]
    public string? TypeScriptVersion { get; init; }

    [JsonPropertyName("projects")]
    public IReadOnlyList<SidecarProject> Projects { get; init; } = [];

    [JsonPropertyName("sourceFiles")]
    public IReadOnlyList<SidecarSourceFile> SourceFiles { get; init; } = [];

    [JsonPropertyName("symbols")]
    public IReadOnlyList<SidecarSymbol> Symbols { get; init; } = [];

    [JsonPropertyName("relationships")]
    public IReadOnlyList<SidecarRelationship> Relationships { get; init; } = [];

    [JsonPropertyName("diagnostics")]
    public IReadOnlyList<SidecarDiagnostic> Diagnostics { get; init; } = [];
}

/// <summary>
/// Describes an observed TypeScript or JavaScript project manifest.
/// </summary>
internal sealed class SidecarProject
{
    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }
}

/// <summary>
/// Describes an analyzed source file.
/// </summary>
internal sealed class SidecarSourceFile
{
    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }
}

/// <summary>
/// Describes a declared symbol resolved by the TypeScript checker.
/// </summary>
internal sealed class SidecarSymbol
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("qualifiedName")]
    public string? QualifiedName { get; init; }

    [JsonPropertyName("symbolKind")]
    public required string SymbolKind { get; init; }

    [JsonPropertyName("nativeKind")]
    public required string NativeKind { get; init; }

    [JsonPropertyName("visibility")]
    public required string Visibility { get; init; }

    [JsonPropertyName("languageId")]
    public required string LanguageId { get; init; }

    [JsonPropertyName("location")]
    public required SidecarLocation Location { get; init; }
}

/// <summary>
/// Describes a one-based source range reported by the sidecar.
/// </summary>
internal sealed class SidecarLocation
{
    [JsonPropertyName("relativePath")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("startLine")]
    public int StartLine { get; init; }

    [JsonPropertyName("startColumn")]
    public int StartColumn { get; init; }

    [JsonPropertyName("endLine")]
    public int EndLine { get; init; }

    [JsonPropertyName("endColumn")]
    public int EndColumn { get; init; }
}

/// <summary>
/// Describes a directed relationship between two sidecar entities.
/// </summary>
internal sealed class SidecarRelationship
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("sourceKind")]
    public required string SourceKind { get; init; }

    [JsonPropertyName("sourceKey")]
    public required string SourceKey { get; init; }

    [JsonPropertyName("targetKind")]
    public required string TargetKind { get; init; }

    [JsonPropertyName("targetKey")]
    public required string TargetKey { get; init; }
}

/// <summary>
/// Describes a limitation the sidecar encountered instead of guessing a fact.
/// </summary>
internal sealed class SidecarDiagnostic
{
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

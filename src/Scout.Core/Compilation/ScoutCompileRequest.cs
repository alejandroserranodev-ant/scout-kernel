namespace Scout.Core.Compilation;

/// <summary>
/// Describes one repository-to-kernel compilation request.
/// </summary>
/// <param name="RepositoryPath">The source repository root.</param>
/// <param name="OutputPath">An optional kernel output directory.</param>
public sealed record ScoutCompileRequest(string RepositoryPath, string? OutputPath = null);

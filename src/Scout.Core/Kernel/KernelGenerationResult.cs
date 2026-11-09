namespace Scout.Core.Kernel;

/// <summary>
/// Reports the location and portable artifact paths of a generated Scout Kernel.
/// </summary>
/// <param name="OutputPath">The absolute kernel output directory.</param>
/// <param name="ArtifactPaths">The sorted paths relative to the kernel root.</param>
public sealed record KernelGenerationResult(string OutputPath, IReadOnlyList<string> ArtifactPaths);

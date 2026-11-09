using Scout.Core.Graph;
using Scout.Core.Intelligence;
using Scout.Core.Ir;
using Scout.Core.Kernel;
using Scout.Core.Repository;

namespace Scout.Core.Compilation;

/// <summary>
/// Exposes the artifacts and intermediate representations produced by one successful compilation.
/// </summary>
/// <param name="Repository">The deterministic repository discovery result.</param>
/// <param name="Structural">The universal structural IR snapshot.</param>
/// <param name="Graph">The validated graph view derived from the structural snapshot.</param>
/// <param name="Intelligence">The evidence-backed project intelligence.</param>
/// <param name="Kernel">The generated Scout Kernel result.</param>
public sealed record ScoutCompilationResult(
    RepositoryDescriptor Repository,
    StructuralSnapshot Structural,
    ProjectGraph Graph,
    IntelligenceSnapshot Intelligence,
    KernelGenerationResult Kernel);

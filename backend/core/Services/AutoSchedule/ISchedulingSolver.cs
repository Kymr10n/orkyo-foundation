using Api.Models;

namespace Api.Services.AutoSchedule;

/// <summary>
/// Solver interface. Implementations compete by <see cref="Priority"/>;
/// the service tries the highest-priority solver first and falls back to lower ones.
/// </summary>
public interface ISchedulingSolver
{
    SolverKind Kind { get; }

    /// <summary>
    /// Higher value = preferred solver. OR-Tools = 100, Greedy = 10.
    /// </summary>
    int Priority { get; }

    Task<SchedulingSolution> SolveAsync(
        AnalyzedSchedulingProblem problem,
        CancellationToken cancellationToken);
}

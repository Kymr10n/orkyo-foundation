using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Precedence edges between requests, and the rules that keep the graph meaningful:
/// leaves only, no self-edges, no duplicates, no cycles.
/// </summary>
public interface IRequestDependencyService
{
    Task<List<RequestDependencyInfo>> GetAllAsync(Guid? siteId, CancellationToken ct = default);
    Task<RequestDependencies> GetForRequestAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>
    /// Adds "<paramref name="successorId"/> waits for request.PredecessorRequestId".
    /// Throws <see cref="NotFoundException"/> when either request is missing and
    /// <see cref="ConflictException"/> for a self-edge, duplicate, non-leaf endpoint or cycle.
    /// </summary>
    Task<RequestDependencyInfo> CreateAsync(Guid successorId, CreateDependencyRequest request, CancellationToken ct = default);

    /// <summary>Removes an edge. Returns false when it does not exist or belongs to another request.</summary>
    Task<bool> DeleteAsync(Guid requestId, Guid dependencyId, CancellationToken ct = default);
}

public class RequestDependencyService : IRequestDependencyService
{
    private readonly IRequestDependencyRepository _repository;
    private readonly IRequestRepository _requests;

    public RequestDependencyService(IRequestDependencyRepository repository, IRequestRepository requests)
    {
        _repository = repository;
        _requests = requests;
    }

    public Task<List<RequestDependencyInfo>> GetAllAsync(Guid? siteId, CancellationToken ct = default)
        => _repository.GetAllAsync(siteId, ct);

    public Task<RequestDependencies> GetForRequestAsync(Guid requestId, CancellationToken ct = default)
        => _repository.GetForRequestAsync(requestId, ct);

    public async Task<RequestDependencyInfo> CreateAsync(
        Guid successorId, CreateDependencyRequest request, CancellationToken ct = default)
    {
        var predecessorId = request.PredecessorRequestId;

        if (predecessorId == successorId)
            throw new ConflictException("A request cannot depend on itself");

        // Shape (non-empty predecessor, non-negative lag) is the validator's job at the
        // boundary — CreateDependencyRequestValidator. What is enforced here is the domain.

        // Leaves only. Summary and container rows carry dates rolled up from their
        // descendants and never reach the scheduler, so an edge on one could not be
        // enforced — it would read as a promise the system cannot keep.
        await EnsureSchedulableLeafAsync(predecessorId, "Predecessor", ct);
        await EnsureSchedulableLeafAsync(successorId, "Successor", ct);

        if (await _repository.ExistsAsync(predecessorId, successorId, ct))
            throw new ConflictException("This dependency already exists");

        if (await _repository.WouldCreateCycleAsync(predecessorId, successorId, ct))
            throw new ConflictException("This dependency would create a circular reference");

        return await _repository.CreateAsync(
            predecessorId, successorId, DependencyTypes.FinishToStart, request.LagMinutes, ct);
    }

    public async Task<bool> DeleteAsync(Guid requestId, Guid dependencyId, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(dependencyId, ct);

        // Deleting through a request that the edge does not touch is a 404, not a silent
        // success: it means the caller is working from a stale view of the graph.
        if (existing is null) return false;
        if (existing.PredecessorRequestId != requestId && existing.SuccessorRequestId != requestId)
            return false;

        return await _repository.DeleteAsync(dependencyId, ct);
    }

    private async Task EnsureSchedulableLeafAsync(Guid requestId, string role, CancellationToken ct)
    {
        var mode = await _requests.GetPlanningModeAsync(requestId, ct)
            ?? throw new NotFoundException("Request", requestId);

        if (mode != PlanningMode.Leaf)
            throw new ConflictException($"{role} must be a schedulable request, not a group");
    }
}

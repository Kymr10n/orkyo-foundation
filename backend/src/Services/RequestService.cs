using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IRequestService
{
    Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false);
    Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false);
    Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true);
    Task<RequestInfo> CreateAsync(CreateRequestRequest request);
    Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);
    Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request);
    Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement);
    Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId);
    Task<List<RequestInfo>> GetChildrenAsync(Guid parentId);
    Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder);
    Task<int> GetDescendantCountAsync(Guid id);
    Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId);
    Task<PlanningMode?> GetPlanningModeAsync(Guid id);
    Task<bool> HasChildrenAsync(Guid id);
    Task<int> DeleteSubtreeAsync(Guid id);
}

public class RequestService : IRequestService
{
    private readonly IRequestRepository _repository;

    public RequestService(IRequestRepository repository)
    {
        _repository = repository;
    }

    public Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false)
        => _repository.GetAllAsync(includeRequirements);

    public Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false)
        => _repository.GetAllAsync(page, includeRequirements);

    public Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true)
        => _repository.GetByIdAsync(id, includeRequirements);

    public async Task<RequestInfo> CreateAsync(CreateRequestRequest request)
    {
        if (request.ParentRequestId.HasValue)
        {
            var parentMode = await _repository.GetPlanningModeAsync(request.ParentRequestId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", request.ParentRequestId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot add children to a leaf request");
        }
        return await _repository.CreateAsync(request);
    }

    public async Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request)
    {
        if (request.ParentRequestId.HasValue)
        {
            if (request.ParentRequestId.Value == id)
                throw new ArgumentException("A request cannot be its own parent");
            var wouldCycle = await _repository.WouldCreateCycleAsync(id, request.ParentRequestId.Value);
            if (wouldCycle) throw new ConflictException("This change would create a circular reference");
            var parentMode = await _repository.GetPlanningModeAsync(request.ParentRequestId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", request.ParentRequestId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot add children to a leaf request");
        }

        if (request.PlanningMode == PlanningMode.Leaf)
        {
            var hasChildren = await _repository.HasChildrenAsync(id);
            if (hasChildren) throw new ConflictException("Cannot change to leaf mode while request has children");
        }

        var existingMode = await _repository.GetPlanningModeAsync(id);
        var effectiveMode = request.PlanningMode ?? existingMode;
        if (effectiveMode != PlanningMode.Leaf && (request.ResourceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue))
            throw new ArgumentException("Only leaf requests can be directly scheduled to a space");

        return await _repository.UpdateAsync(id, request);
    }

    public Task<bool> DeleteAsync(Guid id) => _repository.DeleteAsync(id);

    public Task<bool> ExistsAsync(Guid id) => _repository.ExistsAsync(id);

    public async Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request)
    {
        var isSchedulingPayload = request.ResourceId.HasValue || request.StartTs.HasValue || request.EndTs.HasValue;
        if (isSchedulingPayload)
        {
            var mode = await _repository.GetPlanningModeAsync(id);
            if (mode != null && mode != PlanningMode.Leaf)
                throw new ArgumentException("Only leaf requests can be directly scheduled to a space");
        }
        return await _repository.UpdateScheduleAsync(id, request);
    }

    public Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement)
        => _repository.AddRequirementAsync(requestId, requirement);

    public Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId)
        => _repository.DeleteRequirementAsync(requestId, requirementId);

    public Task<List<RequestInfo>> GetChildrenAsync(Guid parentId)
        => _repository.GetChildrenAsync(parentId);

    public async Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder)
    {
        if (newParentId.HasValue)
        {
            if (newParentId.Value == id)
                throw new ArgumentException("A request cannot be its own parent");
            if (await _repository.WouldCreateCycleAsync(id, newParentId.Value))
                throw new ConflictException("Moving this request would create a circular reference");
            var parentMode = await _repository.GetPlanningModeAsync(newParentId.Value);
            if (parentMode == null) throw new NotFoundException("Parent request", newParentId.Value);
            if (parentMode == PlanningMode.Leaf) throw new ConflictException("Cannot move a request under a leaf request");
        }
        return await _repository.MoveAsync(id, newParentId, sortOrder);
    }

    public Task<int> GetDescendantCountAsync(Guid id)
        => _repository.GetDescendantCountAsync(id);

    public Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId)
        => _repository.WouldCreateCycleAsync(requestId, newParentId);

    public Task<PlanningMode?> GetPlanningModeAsync(Guid id)
        => _repository.GetPlanningModeAsync(id);

    public Task<bool> HasChildrenAsync(Guid id) => _repository.HasChildrenAsync(id);

    public Task<int> DeleteSubtreeAsync(Guid id) => _repository.DeleteSubtreeAsync(id);
}

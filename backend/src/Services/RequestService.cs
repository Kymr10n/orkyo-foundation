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

    public Task<RequestInfo> CreateAsync(CreateRequestRequest request)
        => _repository.CreateAsync(request);

    public Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request)
        => _repository.UpdateAsync(id, request);

    public Task<bool> DeleteAsync(Guid id) => _repository.DeleteAsync(id);

    public Task<bool> ExistsAsync(Guid id) => _repository.ExistsAsync(id);

    public Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request)
        => _repository.UpdateScheduleAsync(id, request);

    public Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement)
        => _repository.AddRequirementAsync(requestId, requirement);

    public Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId)
        => _repository.DeleteRequirementAsync(requestId, requirementId);

    public Task<List<RequestInfo>> GetChildrenAsync(Guid parentId)
        => _repository.GetChildrenAsync(parentId);

    public Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder)
        => _repository.MoveAsync(id, newParentId, sortOrder);

    public Task<int> GetDescendantCountAsync(Guid id)
        => _repository.GetDescendantCountAsync(id);

    public Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId)
        => _repository.WouldCreateCycleAsync(requestId, newParentId);

    public Task<PlanningMode?> GetPlanningModeAsync(Guid id)
        => _repository.GetPlanningModeAsync(id);

    public Task<bool> HasChildrenAsync(Guid id) => _repository.HasChildrenAsync(id);

    public Task<int> DeleteSubtreeAsync(Guid id) => _repository.DeleteSubtreeAsync(id);
}

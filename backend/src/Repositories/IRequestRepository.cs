using Api.Models;

namespace Api.Repositories;

public interface IRequestRepository
{
    Task<List<RequestInfo>> GetAllAsync(bool includeRequirements = false);
    Task<PagedResult<RequestInfo>> GetAllAsync(PageRequest page, bool includeRequirements = false);
    Task<RequestInfo?> GetByIdAsync(Guid id, bool includeRequirements = true);
    Task<RequestInfo> CreateAsync(CreateRequestRequest request);
    Task<RequestInfo?> UpdateAsync(Guid id, UpdateRequestRequest request);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> ExistsAsync(Guid id);

    // Scheduling operations
    Task<List<RequestInfo>> GetScheduledBySiteAsync(Guid siteId);
    Task<RequestInfo?> UpdateScheduleAsync(Guid id, ScheduleRequestRequest request);
    Task<int> BatchUpdateSchedulesAsync(IReadOnlyList<(Guid Id, ScheduleRequestRequest Data)> updates);

    // Requirements operations
    Task<RequestRequirementInfo> AddRequirementAsync(Guid requestId, AddRequirementRequest requirement);
    Task<bool> DeleteRequirementAsync(Guid requestId, Guid requirementId);

    // Tree operations
    Task<List<RequestInfo>> GetChildrenAsync(Guid parentId);
    Task<RequestInfo?> MoveAsync(Guid id, Guid? newParentId, int sortOrder);
    Task<int> GetDescendantCountAsync(Guid id);
    Task<bool> WouldCreateCycleAsync(Guid requestId, Guid newParentId);
    Task<PlanningMode?> GetPlanningModeAsync(Guid id);
    Task<bool> HasChildrenAsync(Guid id);
    Task<int> DeleteSubtreeAsync(Guid id);
}

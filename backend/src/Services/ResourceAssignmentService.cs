using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IResourceAssignmentService
{
    Task<(ResourceAssignmentInfo? Assignment, ResourceConflict? Conflict)> CreateAsync(
        CreateResourceAssignmentRequest request,
        IResourceRequirement? requirement = null);

    Task<bool> CancelAsync(Guid id);
    Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId);
    Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc);
}

public class ResourceAssignmentService(
    IResourceRepository resourceRepository,
    IResourceAssignmentRepository assignmentRepository,
    ICapabilityMatcher capabilityMatcher) : IResourceAssignmentService
{
    public async Task<(ResourceAssignmentInfo? Assignment, ResourceConflict? Conflict)> CreateAsync(
        CreateResourceAssignmentRequest request,
        IResourceRequirement? requirement = null)
    {
        // 1. Resource exists.
        var resource = await resourceRepository.GetByIdAsync(request.ResourceId);
        if (resource is null)
            return (null, Conflict(request.ResourceId, ResourceConflictType.ResourceNotFound,
                "Resource not found"));

        // 2. Resource is active.
        if (!resource.IsActive)
            return (null, Conflict(resource.Id, ResourceConflictType.ResourceInactive,
                "Resource is inactive"));

        // 3. Resource type matches requirement (if provided).
        if (requirement is not null && resource.ResourceTypeId != requirement.ResourceTypeId)
            return (null, Conflict(resource.Id, ResourceConflictType.CapabilityNotApplicable,
                $"Resource type mismatch: expected {requirement.ResourceTypeId}"));

        // 4. Required capabilities present (presence match).
        var requiredIds = requirement?.RequiredCriterionIds ?? [];
        if (requiredIds.Count > 0)
        {
            var satisfied = await capabilityMatcher.ResourceSatisfiesRequirementsAsync(
                resource.Id, requiredIds);
            if (!satisfied)
                return (null, Conflict(resource.Id, ResourceConflictType.CapabilityMissing,
                    "Resource does not satisfy all required capabilities"));
        }

        // 5. Resource not absent (off-times).
        // Site id is not available here in Phase 1 without loading the resource's site.
        // For non-Space resources created in Phase 1 tests (no site affiliation),
        // off-time checking is skipped. Off-time checking for Spaces flows through
        // SchedulingService (unchanged in Phase 1). A full off-time check is wired
        // in Phase 2 when the scheduler switches to resource_assignments.
        // KNOWN LIMITATION (Phase 1): off-time checks not enforced for new resources.

        // 6 + 7. Allocation mode rules.
        switch (resource.AllocationMode)
        {
            case AllocationModes.ConcurrentCapacity:
                return (null, Conflict(resource.Id, ResourceConflictType.InvalidAllocationMode,
                    "ConcurrentCapacity allocation mode is not yet supported"));

            case AllocationModes.Exclusive:
                if (request.AllocationPercent.HasValue)
                    return (null, Conflict(resource.Id, ResourceConflictType.InvalidAllocationPercent,
                        "allocation_percent must be null for Exclusive resources"));

                var overlapping = await assignmentRepository.GetOverlappingActiveAsync(
                    resource.Id, request.StartUtc, request.EndUtc);
                if (overlapping.Count > 0)
                    return (null, new ResourceConflict
                    {
                        ResourceId = resource.Id,
                        Type = ResourceConflictType.ExclusiveOverlap,
                        Message = "Resource is already assigned during this time window",
                        ConflictingAssignmentId = overlapping[0].Id,
                    });
                break;

            case AllocationModes.Fractional:
                if (!request.AllocationPercent.HasValue || request.AllocationPercent.Value <= 0)
                    return (null, Conflict(resource.Id, ResourceConflictType.InvalidAllocationPercent,
                        "allocation_percent is required and must be > 0 for Fractional resources"));

                var existing = await assignmentRepository.GetTotalAllocatedPercentAsync(
                    resource.Id, request.StartUtc, request.EndUtc);
                if (existing + request.AllocationPercent.Value > resource.BaseAvailabilityPercent)
                    return (null, Conflict(resource.Id, ResourceConflictType.FractionalCapacityExceeded,
                        $"Total allocation ({existing + request.AllocationPercent.Value}%) " +
                        $"exceeds available capacity ({resource.BaseAvailabilityPercent}%)"));
                break;
        }

        var assignment = await assignmentRepository.CreateAsync(request);
        return (assignment, null);
    }

    public Task<bool> CancelAsync(Guid id)
        => assignmentRepository.CancelAsync(id);

    public Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId)
        => assignmentRepository.GetByRequestAsync(requestId);

    public Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc)
        => assignmentRepository.GetByResourceAsync(resourceId, fromUtc, toUtc);

    private static ResourceConflict Conflict(Guid resourceId, ResourceConflictType type, string message) =>
        new() { ResourceId = resourceId, Type = type, Message = message };
}

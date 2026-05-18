using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface IResourceAssignmentService
{
    Task<(ResourceAssignmentInfo? Assignment, ResourceConflict? Conflict)> CreateAsync(
        CreateResourceAssignmentRequest request,
        IResourceRequirement? requirement = null, CancellationToken ct = default);

    Task<bool> CancelAsync(Guid id, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default);
    Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}

/// <summary>
/// Thin delegation layer over <see cref="IResourceAssignmentValidator"/>.
/// Owns no validation logic of its own; just translates the first blocker into
/// the legacy <see cref="ResourceConflict"/> shape for existing callers.
/// </summary>
public class ResourceAssignmentService(
    IResourceAssignmentRepository assignmentRepository,
    IResourceAssignmentValidator validator) : IResourceAssignmentService
{
    // Single source of truth for ValidationReasonCode → ResourceConflictType mapping.
    // Adding a new ValidationReasonCode requires adding a row here (compile-time guard
    // via the explicit dictionary keys; unknown codes throw rather than silently mapping
    // to a wrong default like the old switch did).
    private static readonly Dictionary<ValidationReasonCode, ResourceConflictType> ConflictTypeByCode = new()
    {
        [ValidationReasonCode.ResourceNotFound] = ResourceConflictType.ResourceNotFound,
        [ValidationReasonCode.ResourceInactive] = ResourceConflictType.ResourceInactive,
        [ValidationReasonCode.ResourceTypeMismatch] = ResourceConflictType.CapabilityNotApplicable,
        [ValidationReasonCode.CapabilityMissing] = ResourceConflictType.CapabilityMissing,
        [ValidationReasonCode.OffTimeOverlap] = ResourceConflictType.OffTimeOverlap,
        [ValidationReasonCode.AssignmentOverbooked] = ResourceConflictType.ExclusiveOverlap, // refined below
        [ValidationReasonCode.InvalidAllocationMode] = ResourceConflictType.InvalidAllocationMode,
        [ValidationReasonCode.InvalidAllocationPercent] = ResourceConflictType.InvalidAllocationPercent,
    };

    public async Task<(ResourceAssignmentInfo? Assignment, ResourceConflict? Conflict)> CreateAsync(
        CreateResourceAssignmentRequest request,
        IResourceRequirement? requirement = null, CancellationToken ct = default)
    {
        var validationRequest = new ValidateResourceAssignmentRequest
        {
            RequestId = request.RequestId,
            ResourceId = request.ResourceId,
            StartUtc = request.StartUtc,
            EndUtc = request.EndUtc,
            AllocationPercent = request.AllocationPercent,
            AllocationUnits = request.AllocationUnits,
            AllocationMode = null // Use resource's default
        };

        var result = await validator.ValidateAsync(validationRequest);

        if (result.Severity == ValidationSeverity.Blocker && result.Blockers.Count > 0)
            return (null, ToConflict(result.Blockers[0], request.ResourceId));

        var assignment = await assignmentRepository.CreateAsync(request);
        return (assignment, null);
    }

    public Task<bool> CancelAsync(Guid id, CancellationToken ct = default) => assignmentRepository.CancelAsync(id, ct);

    public Task<List<ResourceAssignmentInfo>> GetByRequestAsync(Guid requestId, CancellationToken ct = default)
        => assignmentRepository.GetByRequestAsync(requestId, ct);

    public Task<List<ResourceAssignmentInfo>> GetByResourceAsync(Guid resourceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        => assignmentRepository.GetByResourceAsync(resourceId, fromUtc, toUtc, ct);

    private static ResourceConflict ToConflict(ValidationIssue issue, Guid fallbackResourceId)
    {
        if (!ConflictTypeByCode.TryGetValue(issue.Code, out var type))
            throw new InvalidOperationException(
                $"Unmapped ValidationReasonCode '{issue.Code}'. Add it to ConflictTypeByCode.");

        // AssignmentOverbooked covers both exclusive collision and fractional over-capacity.
        // The latter is identified by its message; refine the conflict type for callers.
        if (issue.Code == ValidationReasonCode.AssignmentOverbooked
            && issue.Message.Contains("exceeds available capacity"))
        {
            type = ResourceConflictType.FractionalCapacityExceeded;
        }

        return new ResourceConflict
        {
            ResourceId = issue.ResourceId ?? fallbackResourceId,
            Type = type,
            Message = issue.Message,
            ConflictingAssignmentId = issue.ConflictingAssignmentId,
            ConflictingOffTimeId = issue.ConflictingOffTimeId,
        };
    }
}

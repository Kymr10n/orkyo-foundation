using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Sets a criterion value on a resource or a resource group. One path for both, so the checks
/// cannot drift again: the criterion must exist (404), the value must fit the criterion's type
/// and constraints (400, <see cref="ICriterionValueValidator"/>), and the criterion must apply to
/// the owner's resource type (400, checked by the repository with the shared rule in
/// <c>CriterionScopeSql.AppliesTo</c>). A missing resource or group is a 404 from the repository.
/// </summary>
public interface ICapabilityAssignmentService
{
    Task<ResourceCapabilityInfo> SetResourceCapabilityAsync(Guid resourceId, Guid criterionId, JsonElement value, CancellationToken ct = default);
    Task<GroupCapabilityInfo> SetGroupCapabilityAsync(Guid groupId, Guid criterionId, JsonElement value, CancellationToken ct = default);
}

public class CapabilityAssignmentService(
    ICriteriaRepository criteria,
    ICriterionValueValidator valueValidator,
    IResourceCapabilityRepository resourceCapabilities,
    IGroupCapabilityRepository groupCapabilities) : ICapabilityAssignmentService
{
    public async Task<ResourceCapabilityInfo> SetResourceCapabilityAsync(
        Guid resourceId, Guid criterionId, JsonElement value, CancellationToken ct = default)
    {
        await EnsureValueFitsAsync(criterionId, value, ct);
        return await resourceCapabilities.UpsertAsync(resourceId, criterionId, value, ct);
    }

    public async Task<GroupCapabilityInfo> SetGroupCapabilityAsync(
        Guid groupId, Guid criterionId, JsonElement value, CancellationToken ct = default)
    {
        await EnsureValueFitsAsync(criterionId, value, ct);
        return await groupCapabilities.UpsertAsync(groupId, criterionId, value, ct);
    }

    // Values used to be stored as raw JSONB with no type check: a Number criterion would accept
    // "banana" and only misbehave later, as a silent non-match in the solver.
    private async Task EnsureValueFitsAsync(Guid criterionId, JsonElement value, CancellationToken ct)
    {
        var criterion = await criteria.GetByIdAsync(criterionId, ct)
            ?? throw new NotFoundException("Criterion", criterionId);
        if (valueValidator.Validate(criterion, value) is { } invalid)
            throw new ArgumentException(invalid);
    }
}

using System.Text.Json;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ICapabilityMatcher
{
    Task<bool> ResourceSatisfiesRequirementsAsync(
        Guid resourceId,
        IReadOnlyList<Guid> requiredCriterionIds);
}

public class CapabilityMatcher(IResourceCapabilityRepository capabilityRepository) : ICapabilityMatcher
{
    public async Task<bool> ResourceSatisfiesRequirementsAsync(
        Guid resourceId,
        IReadOnlyList<Guid> requiredCriterionIds)
    {
        if (requiredCriterionIds.Count == 0)
            return true;

        var capabilities = await capabilityRepository.GetByResourceAsync(resourceId);
        var presentIds = capabilities.Select(c => c.CriterionId).ToHashSet();

        // Phase 1: presence match only. Typed operator matching (≥/≤/= for Number,
        // Enum membership, String equality) is added in Phase 3.
        return requiredCriterionIds.All(presentIds.Contains);
    }
}

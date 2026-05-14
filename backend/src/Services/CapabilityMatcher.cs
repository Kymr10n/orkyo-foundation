using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ICapabilityMatcher
{
    Task<bool> ResourceSatisfiesRequirementsAsync(
        Guid resourceId,
        IReadOnlyList<Guid> requiredCriterionIds);

    // Phase 3: Typed operator matching
    Task<bool> ResourceSatisfiesRequirementAsync(
        Guid resourceId,
        RequestRequirementInfo requirement);
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

        // Fallback to presence match for backwards compatibility
        return requiredCriterionIds.All(presentIds.Contains);
    }

    // Phase 3: Typed operator matching (≥/≤/= for Number, Enum membership, String equality, Boolean)
    public async Task<bool> ResourceSatisfiesRequirementAsync(
        Guid resourceId,
        RequestRequirementInfo requirement)
    {
        var capabilities = await capabilityRepository.GetByResourceAsync(resourceId);
        var capability = capabilities.FirstOrDefault(c => c.CriterionId == requirement.CriterionId);

        if (capability is null)
            return false; // Capability not present on resource

        // If the requirement has no operator/allowed values, fall back to presence match
        if (requirement.Operator is null && requirement.AllowedValues is null)
            return true;

        var capValue = capability.Value;
        var reqValue = requirement.Value;
        var criterionType = capability.Criterion?.DataType;

        // Type-specific matching based on criterion data type (from capability.Criterion)
        if (criterionType == CriterionDataType.Boolean)
        {
            // Boolean: resource value must be true
            return capValue.ValueKind == JsonValueKind.True;
        }
        else if (criterionType == CriterionDataType.Number)
        {
            // Number: apply operator (≥, ≤, =)
            if (requirement.Operator is null)
                return true; // No operator specified, presence is enough

            if (!capValue.TryGetDouble(out var resourceValue) ||
                !reqValue.TryGetDouble(out var requiredValue))
                return false;

            return requirement.Operator switch
            {
                ">=" => resourceValue >= requiredValue,
                "<=" => resourceValue <= requiredValue,
                "=" => Math.Abs(resourceValue - requiredValue) < double.Epsilon,
                _ => false // Unknown operator
            };
        }
        else if (criterionType == CriterionDataType.Enum)
        {
            // Enum: resource value must be in allowed set
            var allowedValues = requirement.AllowedValues;
            if (allowedValues is null || allowedValues.Value.ValueKind != JsonValueKind.Array)
                return true; // No allowed values specified, presence is enough

            var resourceValue = capValue.GetRawText();
            var allowed = new HashSet<string>();
            foreach (var item in allowedValues.Value.EnumerateArray())
                allowed.Add(item.GetRawText());
            return allowed.Contains(resourceValue);
        }
        else if (criterionType == CriterionDataType.String)
        {
            // String: case-insensitive equality
            if (requirement.Operator is null)
                return true; // No operator, presence is enough

            var resourceValue = capValue.GetRawText();
            var requiredValue = reqValue.GetRawText();
            return string.Equals(resourceValue, requiredValue, StringComparison.OrdinalIgnoreCase);
        }

        return true; // Unknown data type, fall back to presence match
    }
}

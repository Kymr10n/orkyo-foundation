using System.Text.Json;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

public interface ICapabilityMatcher
{
    Task<bool> ResourceSatisfiesRequirementsAsync(
        Guid resourceId,
        IReadOnlyList<Guid> requiredCriterionIds, CancellationToken ct = default);

    // Phase 3: Typed operator matching
    Task<bool> ResourceSatisfiesRequirementAsync(
        Guid resourceId,
        RequestRequirementInfo requirement, CancellationToken ct = default);

    /// <summary>
    /// Pure typed-operator match against an already-loaded capability set — no I/O. Lets callers
    /// that have preloaded capabilities (e.g. batch validation) match in memory.
    /// </summary>
    bool Satisfies(IReadOnlyList<ResourceCapabilityInfo> capabilities, RequestRequirementInfo requirement);
}

public class CapabilityMatcher(IResourceCapabilityRepository capabilityRepository) : ICapabilityMatcher
{
    public async Task<bool> ResourceSatisfiesRequirementsAsync(
        Guid resourceId,
        IReadOnlyList<Guid> requiredCriterionIds, CancellationToken ct = default)
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
        RequestRequirementInfo requirement, CancellationToken ct = default)
    {
        var capabilities = await capabilityRepository.GetByResourceAsync(resourceId);
        return Satisfies(capabilities, requirement);
    }

    public bool Satisfies(IReadOnlyList<ResourceCapabilityInfo> capabilities, RequestRequirementInfo requirement)
    {
        var capability = capabilities.FirstOrDefault(c => c.CriterionId == requirement.CriterionId);

        if (capability is null)
            return false; // Capability not present on resource

        var capValue = capability.Value;
        var reqValue = requirement.Value;
        var criterionType = capability.Criterion?.DataType;

        // Boolean always compares values — it uses neither Operator nor AllowedValues,
        // so it must be evaluated before the presence-match fallback below.
        if (criterionType == CriterionDataType.Boolean)
        {
            return capValue.ValueKind == reqValue.ValueKind
                   && capValue.ValueKind is JsonValueKind.True or JsonValueKind.False;
        }

        // For all other types: if the requirement has no operator/allowed values, fall back to presence match
        if (requirement.Operator is null && requirement.AllowedValues is null)
            return true;
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

using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// The active requests overlapping a window that a resource could take on, each requirement
/// marked satisfied or not by that resource's capabilities.
/// </summary>
public interface ICandidateRequestService
{
    /// <summary>Null when the resource does not exist.</summary>
    Task<List<CandidateRequestInfo>?> GetForResourceAsync(
        Guid resourceId, DateTime start, DateTime end, CancellationToken ct = default);
}

public class CandidateRequestService(
    IResourceService resources,
    IRequestRepository requests,
    IResourceCapabilityRepository capabilities,
    ICapabilityMatcher matcher) : ICandidateRequestService
{
    public async Task<List<CandidateRequestInfo>?> GetForResourceAsync(
        Guid resourceId, DateTime start, DateTime end, CancellationToken ct = default)
    {
        var resource = await resources.GetByIdAsync(resourceId, ct);
        if (resource is null) return null;

        var candidates = await requests.GetCandidatesOverlappingAsync(resourceId, start, end, ct);

        // One read of the resource's capabilities, matched in memory. The endpoint used to await
        // the matcher once per requirement of every candidate, each call re-reading the same set.
        var owned = await capabilities.GetByResourceAsync(resourceId, ct);

        return candidates.Select(c => new CandidateRequestInfo(
            c.Request.Id, c.Request.Name, c.Request.StartTs, c.Request.EndTs,
            (c.Request.Requirements ?? [])
                // A requirement scoped to another resource type is not this resource's to satisfy.
                .Where(r => r.AppliesTo(resource.ResourceTypeKey))
                .Select(r => new CandidateRequirementInfo(
                    r.Criterion?.Name ?? r.CriterionId.ToString(), matcher.Satisfies(owned, r)))
                .ToList(),
            c.AssignmentId)).ToList();
    }
}

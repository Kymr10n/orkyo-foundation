using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Resolves blocked periods for resources by unioning resource absences with
/// closing availability events (after applying scope-override precedence:
/// resource > resource_group > resource_type > event.default_effect).
///
/// Availability events only affect site-bound resources (spaces, site-bound equipment).
/// People resources (no site FK in person_profiles) are governed exclusively by their
/// resource absences.
/// </summary>
public interface IAvailabilityResolver
{
    Task<List<BlockedPeriod>> GetBlockedPeriodsAsync(Guid resourceId, CancellationToken ct = default);

    /// <summary>
    /// Returns blocked periods for every resource in <paramref name="resourceIds"/>,
    /// applying availability events from <paramref name="siteId"/>.
    /// Used by the auto-scheduler to pre-compute the full problem state.
    /// </summary>
    Task<Dictionary<Guid, List<BlockedPeriod>>> GetBlockedPeriodsForResourcesAsync(
        Guid siteId, IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}

public class AvailabilityResolver(
    IAvailabilityEventRepository eventRepository,
    IResourceAbsenceRepository absenceRepository,
    ISchedulingRepository schedulingRepository,
    IResourceGroupMemberRepository groupMemberRepository) : IAvailabilityResolver
{
    public async Task<List<BlockedPeriod>> GetBlockedPeriodsAsync(Guid resourceId, CancellationToken ct = default)
    {
        var absences = await absenceRepository.GetByResourceAsync(resourceId, ct);
        var blocked = AbsencesToBlockedPeriods(absences);

        var siteId = await schedulingRepository.GetSiteIdForResourceAsync(resourceId, ct);
        if (siteId.HasValue)
        {
            var events = await eventRepository.GetEnabledBySiteWithScopesAsync(siteId.Value, ct);
            var groupIds = await groupMemberRepository.GetGroupIdsForResourceAsync(resourceId, ct);
            var resourceTypeIds = await schedulingRepository.GetResourceTypeIdsAsync([resourceId], ct);
            var resourceTypeId = resourceTypeIds.GetValueOrDefault(resourceId);

            foreach (var ev in events)
            {
                var effect = ResolveEffect(ev, resourceId, groupIds, resourceTypeId == Guid.Empty ? null : resourceTypeId);
                if (effect == ScopeEffect.Closed || (effect == null && ev.DefaultEffect == DefaultEffect.Closed))
                    blocked.Add(EventToBlockedPeriod(ev));
            }
        }

        return blocked;
    }

    public async Task<Dictionary<Guid, List<BlockedPeriod>>> GetBlockedPeriodsForResourcesAsync(
        Guid siteId, IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        var result = resourceIds.ToDictionary(id => id, _ => new List<BlockedPeriod>());

        var absenceMap = await absenceRepository.GetEnabledByResourcesAsync(resourceIds, ct);
        foreach (var (resourceId, absences) in absenceMap)
            result[resourceId].AddRange(AbsencesToBlockedPeriods(absences));

        var events = await eventRepository.GetEnabledBySiteWithScopesAsync(siteId, ct);
        if (events.Count == 0) return result;

        // Batch-load group memberships for all resources
        var groupMembershipMap = await groupMemberRepository.GetGroupIdsForResourcesAsync(resourceIds, ct);
        var resourceTypeMap = await schedulingRepository.GetResourceTypeIdsAsync(resourceIds, ct);

        foreach (var resourceId in resourceIds)
        {
            var groupIds = groupMembershipMap.GetValueOrDefault(resourceId, []);
            var resourceTypeId = resourceTypeMap.GetValueOrDefault(resourceId);
            foreach (var ev in events)
            {
                var effect = ResolveEffect(ev, resourceId, groupIds, resourceTypeId == Guid.Empty ? null : resourceTypeId);
                if (effect == ScopeEffect.Closed || (effect == null && ev.DefaultEffect == DefaultEffect.Closed))
                    result[resourceId].Add(EventToBlockedPeriod(ev));
            }
        }

        return result;
    }

    // ── Precedence: resource scope > resource_group scope > resource_type scope > default ──

    private static ScopeEffect? ResolveEffect(
        AvailabilityEventInfo ev,
        Guid resourceId,
        IReadOnlyList<Guid> groupIds,
        Guid? resourceTypeId)
    {
        // 1. Resource-level override
        var resourceScope = ev.Scopes.FirstOrDefault(s =>
            s.TargetType == ScopeTargetType.Resource && s.TargetId == resourceId);
        if (resourceScope != null) return resourceScope.Effect;

        // 2. Resource-group override (first matching group wins)
        foreach (var groupId in groupIds)
        {
            var groupScope = ev.Scopes.FirstOrDefault(s =>
                s.TargetType == ScopeTargetType.ResourceGroup && s.TargetId == groupId);
            if (groupScope != null) return groupScope.Effect;
        }

        // 3. Resource-type override
        if (resourceTypeId.HasValue)
        {
            var typeScope = ev.Scopes.FirstOrDefault(s =>
                s.TargetType == ScopeTargetType.ResourceType && s.TargetId == resourceTypeId.Value);
            if (typeScope != null) return typeScope.Effect;
        }

        // 4. No override → caller uses default_effect
        return null;
    }

    private static List<BlockedPeriod> AbsencesToBlockedPeriods(IEnumerable<ResourceAbsenceInfo> absences)
        => absences
            .Where(a => a.Enabled)
            .Select(a => new BlockedPeriod
            {
                Id = a.Id,
                StartTs = a.StartTs,
                EndTs = a.EndTs,
                Title = a.Title,
                Source = BlockedPeriodSource.ResourceAbsence,
                AbsenceType = a.AbsenceType,
            })
            .ToList();

    private static BlockedPeriod EventToBlockedPeriod(AvailabilityEventInfo ev) => new()
    {
        Id = ev.Id,
        StartTs = ev.StartTs,
        EndTs = ev.EndTs,
        Title = ev.Title,
        Source = BlockedPeriodSource.AvailabilityEvent,
        EventType = ev.EventType,
    };
}

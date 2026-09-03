using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Resolves blocked periods for resources by unioning resource absences with
/// closing availability events (after applying scope-override precedence:
/// resource > resource_group > resource_type > event.default_effect).
///
/// Availability events only affect resources anchored to a site. Resources without a home
/// site are governed exclusively by their resource absences.
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

    /// <summary>
    /// Returns blocked periods for every resource in <paramref name="resourceIds"/>, resolving each
    /// resource's anchoring site individually. Unlike the single-site overload (auto-scheduler), this
    /// supports resources spanning multiple sites and people with no site at all — the shape the
    /// insights/utilization aggregates need. Semantically identical to calling
    /// <see cref="GetBlockedPeriodsAsync"/> per resource, but loads absences, site resolution,
    /// group/type metadata, and each distinct site's events in bulk instead of per resource.
    /// </summary>
    Task<Dictionary<Guid, List<BlockedPeriod>>> GetBlockedPeriodsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);

    /// <summary>
    /// Scheduling settings per resource, resolved through each resource's anchoring site —
    /// the working-hours/weekend mask that turns a wall-clock period into bookable capacity.
    /// Sits here because this class already resolves resource → site in bulk, so the
    /// insights and utilization aggregates get the mask without either growing a
    /// scheduling-repository dependency of its own.
    ///
    /// Resources with no home site, and sites with no settings row, are absent from the
    /// result: callers treat a missing entry as 24/7, which is the pre-mask behaviour.
    /// </summary>
    Task<Dictionary<Guid, SchedulingSettingsInfo>> GetSchedulingSettingsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default);
}

public class AvailabilityResolver(
    IAvailabilityEventRepository eventRepository,
    IResourceAbsenceRepository absenceRepository,
    ISchedulingRepository schedulingRepository,
    IResourceGroupMemberRepository groupMemberRepository) : IAvailabilityResolver
{
    public async Task<List<BlockedPeriod>> GetBlockedPeriodsAsync(Guid resourceId, CancellationToken ct = default)
    {
        // Delegate to the multi-resource resolver (its doc contract is "semantically identical
        // to calling this per resource") so the absence→blocked + event-scope assembly lives in
        // exactly one place. Both paths filter to enabled absences (AbsencesToBlockedPeriods).
        var byResource = await GetBlockedPeriodsForResourcesAsync([resourceId], ct);
        return byResource[resourceId];
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
        var holidaysEnabled = await HolidaysEnabledAsync(siteId, ct);

        // Batch-load group memberships for all resources
        var groupMembershipMap = await groupMemberRepository.GetGroupIdsForResourcesAsync(resourceIds, ct);
        var resourceTypeMap = await schedulingRepository.GetResourceTypeIdsAsync(resourceIds, ct);

        foreach (var resourceId in resourceIds)
        {
            AddBlockingEvents(result[resourceId], resourceId, events, holidaysEnabled,
                groupMembershipMap.GetValueOrDefault(resourceId, []),
                resourceTypeMap.GetValueOrDefault(resourceId));
        }

        return result;
    }

    public async Task<Dictionary<Guid, List<BlockedPeriod>>> GetBlockedPeriodsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        var result = resourceIds.ToDictionary(id => id, _ => new List<BlockedPeriod>());
        if (resourceIds.Count == 0) return result;

        // Absences apply to every resource regardless of site (people are governed solely by these).
        var absenceMap = await absenceRepository.GetEnabledByResourcesAsync(resourceIds, ct);
        foreach (var (resourceId, absences) in absenceMap)
            result[resourceId].AddRange(AbsencesToBlockedPeriods(absences));

        // Availability events are site-bound; resolve each resource's anchoring site. Unsited
        // resources (people without a home site, etc.) are omitted here → governed by absences only.
        var siteMap = await schedulingRepository.GetSiteIdsForResourcesAsync(resourceIds, ct);
        if (siteMap.Count == 0) return result;

        var groupMembershipMap = await groupMemberRepository.GetGroupIdsForResourcesAsync(resourceIds, ct);
        var resourceTypeMap = await schedulingRepository.GetResourceTypeIdsAsync(resourceIds, ct);

        // Load each distinct site's enabled events + holiday setting once (sites ≪ resources).
        var eventsBySite = new Dictionary<Guid, List<AvailabilityEventInfo>>();
        var holidaysEnabledBySite = new Dictionary<Guid, bool>();
        foreach (var siteId in siteMap.Values.Distinct())
        {
            eventsBySite[siteId] = await eventRepository.GetEnabledBySiteWithScopesAsync(siteId, ct);
            holidaysEnabledBySite[siteId] = await HolidaysEnabledAsync(siteId, ct);
        }

        foreach (var (resourceId, siteId) in siteMap)
        {
            var events = eventsBySite[siteId];
            if (events.Count == 0) continue;

            var holidaysEnabled = holidaysEnabledBySite[siteId];
            AddBlockingEvents(result[resourceId], resourceId, events, holidaysEnabled,
                groupMembershipMap.GetValueOrDefault(resourceId, []),
                resourceTypeMap.GetValueOrDefault(resourceId));
        }

        return result;
    }

    public async Task<Dictionary<Guid, SchedulingSettingsInfo>> GetSchedulingSettingsForResourcesAsync(
        IReadOnlyList<Guid> resourceIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, SchedulingSettingsInfo>();
        if (resourceIds.Count == 0) return result;

        var siteMap = await schedulingRepository.GetSiteIdsForResourcesAsync(resourceIds, ct);
        if (siteMap.Count == 0) return result;

        // One load for every distinct site (sites ≪ resources), same shape as the events above.
        var settingsBySite = await schedulingRepository.GetSettingsBySitesAsync(
            siteMap.Values.Distinct().ToList(), ct);

        foreach (var (resourceId, siteId) in siteMap)
        {
            if (settingsBySite.TryGetValue(siteId, out var settings))
                result[resourceId] = settings;
        }

        return result;
    }

    /// <summary>
    /// The events-to-blocked-periods step, shared by the two resolver overloads.
    ///
    /// Only this inner loop is shared. The overloads themselves are NOT interchangeable:
    /// the site-scoped one applies the caller's site to every resource, including resources
    /// with no home site of their own, while the site-resolving one omits unsited resources
    /// so they end up governed by absences alone. Collapsing them would silently change
    /// which resources see a site's closures.
    /// </summary>
    private static void AddBlockingEvents(
        List<BlockedPeriod> into,
        Guid resourceId,
        IReadOnlyList<AvailabilityEventInfo> events,
        bool holidaysEnabled,
        IReadOnlyList<Guid> groupIds,
        Guid resourceTypeId)
    {
        foreach (var ev in events)
        {
            var effect = ResolveEffect(ev, resourceId, groupIds, resourceTypeId == Guid.Empty ? null : resourceTypeId);
            if (ShouldBlock(ev, effect, holidaysEnabled))
                into.Add(EventToBlockedPeriod(ev));
        }
    }

    private async Task<bool> HolidaysEnabledAsync(Guid siteId, CancellationToken ct)
        => (await schedulingRepository.GetSettingsAsync(siteId, ct))?.PublicHolidaysEnabled ?? false;

    // A closing event blocks the resource — except public holidays, which apply only when the
    // site opts into them. This is the single gate for holiday suppression: every consumer
    // (conflicts, auto-scheduler, insights, utilization) sees the same filtered set.
    private static bool ShouldBlock(AvailabilityEventInfo ev, ScopeEffect? effect, bool holidaysEnabled)
    {
        var closed = effect == ScopeEffect.Closed || (effect == null && ev.DefaultEffect == DefaultEffect.Closed);
        if (!closed) return false;
        return ev.EventType != AvailabilityEventType.PublicHoliday || holidaysEnabled;
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

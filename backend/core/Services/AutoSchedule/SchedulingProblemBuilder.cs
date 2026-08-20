using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public class SchedulingProblemBuilder
{
    private readonly IRequestRepository _requestRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceCapabilityRepository _capabilityRepository;
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IAvailabilityResolver _resolver;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        IResourceRepository resourceRepository,
        IResourceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository,
        IAvailabilityResolver resolver)
    {
        _requestRepository = requestRepository;
        _resourceRepository = resourceRepository;
        _capabilityRepository = capabilityRepository;
        _schedulingRepository = schedulingRepository;
        _resolver = resolver;
    }

    public virtual async Task<SchedulingProblem> BuildAsync(
        AutoSchedulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(request.SiteId, cancellationToken);

        // The schedulable backlog is every leaf that isn't fully scheduled, in two disjoint fetches
        // that together reproduce the old tenant-wide `!IsScheduled` leaf filter without the heavy
        // GetAllAsync (which pulled every request, groups and finished ones included):
        //   • GetUnscheduledAsync — leaves with start_ts IS NULL (the drag-to-schedule backlog).
        //   • GetPartiallyScheduledLeavesAsync — leaves WITH a start_ts but still !IsScheduled
        //     (no end_ts, or a target type with no assignment). These are excluded from both the
        //     unscheduled backlog and the fixed-occupancy fetch, so without this second set
        //     they'd be invisible to the solver despite being auto-schedulable before.
        var unscheduled = await _requestRepository.GetUnscheduledAsync(
            includeRequirements: true, ct: cancellationToken);
        var partiallyScheduled = await _requestRepository.GetPartiallyScheduledLeavesAsync(
            includeRequirements: true, ct: cancellationToken);

        // One run solves one resource type: the pool is a single type, and the solver's
        // no-overlap-per-node model has nothing to say about matching a room to a van. A request
        // needing both is scheduled by two runs, one per type, each filling its own slot.
        // The type is resolved by AutoScheduleService before this is called; a null here means a
        // caller skipped that resolution, and guessing a default would hide it.
        var targetTypeKey = request.ResourceTypeKey
            ?? throw new ArgumentException(
                "ResourceTypeKey must be resolved before building the problem.", nameof(request));

        var eligibleRequests = unscheduled
            .Concat(partiallyScheduled)
            .Where(r => r.Status is RequestStatus.New or RequestStatus.InProgress)
            .Where(r => r.MinimalDurationValue > 0)
            // Only requests that want this type and have not already got one. Without the second
            // test a request whose room is already booked would be offered another one.
            .Where(r => r.TargetResourceTypeKeys.Contains(targetTypeKey))
            .Where(r => r.GetResourceIdForType(targetTypeKey) is null);

        if (request.RequestIds is { Count: > 0 })
        {
            var requestIdSet = request.RequestIds.ToHashSet();
            eligibleRequests = eligibleRequests.Where(r => requestIdSet.Contains(r.Id));
        }

        // Window the site filter to the horizon. Without it the filter resolves a travelling
        // resource's location as of now(), so solving three months out included or excluded
        // people and tools by where they happen to be today — and the same run tomorrow
        // produced a different pool, and a different fingerprint.
        // Every candidate: a pool silently cut at 1000 would make the solver produce a valid
        // schedule over the wrong set, and change the run fingerprint for no visible reason.
        var candidates = await _resourceRepository.GetEveryAsync(
            new ResourceListFilter
            {
                ResourceTypeKey = targetTypeKey,
                SiteId = request.SiteId,
                SiteWindowFrom = request.HorizonStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                SiteWindowTo = request.HorizonEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                IsActive = true,
            },
            cancellationToken);
        var capabilitiesByResource = (await _capabilityRepository.GetByResourcesAsync(
                candidates.Select(c => c.Id).ToList(), cancellationToken))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.CriterionId).ToHashSet());
        var resourceNodes = candidates
            .Select(c => new ResourceNode(c.Id, c.Name, capabilitiesByResource.GetValueOrDefault(c.Id) ?? []))
            .ToList();

        var candidateIds = resourceNodes.Select(n => n.ResourceId).ToList();
        var blockedPeriodsByResource = await _resolver.GetBlockedPeriodsForResourcesAsync(
            request.SiteId, candidateIds, cancellationToken);

        var requestNodes = new List<RequestNode>();
        foreach (var r in eligibleRequests)
        {
            var durationDays = DurationToDays(r.MinimalDurationValue, r.MinimalDurationUnit, settings);
            if (durationDays <= 0) continue;

            requestNodes.Add(new RequestNode(
                r.Id,
                r.Name,
                r.EarliestStartTs.HasValue ? DateOnly.FromDateTime(r.EarliestStartTs.Value) : (DateOnly?)null,
                r.LatestEndTs.HasValue ? DateOnly.FromDateTime(r.LatestEndTs.Value) : (DateOnly?)null,
                durationDays,
                Priority: (int)r.Status,
                r.SchedulingSettingsApply,
                r.Requirements?.Select(req => req.CriterionId).ToHashSet() ?? new HashSet<Guid>()));
        }

        // Fixed occupancies: requests in this site whose bar can touch the horizon. The solvers
        // only consult occupancies on the candidate resources within the horizon, so the
        // site+window fetch is solver-equivalent to the previous tenant-wide scan. The upper bound
        // is exclusive-day so an assignment starting late on the last horizon day is still seen.
        // No scheduling_settings_apply filter — manually scheduled requests occupy resources too.
        var scheduled = await _requestRepository.GetScheduledBySiteWindowAsync(
            request.SiteId,
            request.HorizonStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            request.HorizonEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            cancellationToken);
        // Holding a resource of this type is what occupies it — not being fully scheduled. A
        // request still waiting on its technician has its room booked all the same, and offering
        // that room to someone else would double-book it.
        var fixedAssignments = scheduled
            .Where(r => r.StartTs.HasValue && r.EndTs.HasValue)
            .Select(r => (Request: r, ResourceId: r.GetResourceIdForType(targetTypeKey)))
            .Where(x => x.ResourceId.HasValue)
            .Select(x => new FixedOccupancy(
                x.Request.Id,
                x.ResourceId!.Value,
                DateOnly.FromDateTime(x.Request.StartTs!.Value),
                DateOnly.FromDateTime(x.Request.EndTs!.Value)))
            .ToList();

        return new SchedulingProblem(
            request.SiteId, request.HorizonStart, request.HorizonEnd,
            requestNodes, resourceNodes, fixedAssignments,
            settings, blockedPeriodsByResource);
    }

    private static int DurationToDays(int value, DurationUnit unit, SchedulingSettingsInfo? settings)
    {
        var minutesPerDay = 24 * 60;
        if (settings is { WorkingHoursEnabled: true })
        {
            minutesPerDay = (int)(settings.WorkingDayEnd - settings.WorkingDayStart).TotalMinutes;
            if (minutesPerDay <= 0) minutesPerDay = 8 * 60;
        }
        var totalMinutes = SchedulingEngine.DurationToMinutes(value, unit);
        return Math.Max(1, (int)Math.Ceiling((double)totalMinutes / minutesPerDay));
    }
}

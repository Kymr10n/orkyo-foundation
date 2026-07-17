using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public class SchedulingProblemBuilder
{
    private readonly IRequestRepository _requestRepository;
    private readonly ISpaceRepository _spaceRepository;
    private readonly IResourceCapabilityRepository _capabilityRepository;
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IAvailabilityResolver _resolver;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        ISpaceRepository spaceRepository,
        IResourceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository,
        IAvailabilityResolver resolver)
    {
        _requestRepository = requestRepository;
        _spaceRepository = spaceRepository;
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
        //     (no end_ts, or no Space assignment). These timed-but-spaceless leaves are excluded
        //     from both the unscheduled backlog and the fixed-occupancy fetch, so without this
        //     second set they'd be invisible to the solver despite being auto-schedulable before.
        var unscheduled = await _requestRepository.GetUnscheduledAsync(
            includeRequirements: true, ct: cancellationToken);
        var partiallyScheduled = await _requestRepository.GetPartiallyScheduledLeavesAsync(
            includeRequirements: true, ct: cancellationToken);

        var eligibleRequests = unscheduled
            .Concat(partiallyScheduled)
            .Where(r => r.Status is RequestStatus.New or RequestStatus.InProgress)
            .Where(r => r.MinimalDurationValue > 0);

        if (request.RequestIds is { Count: > 0 })
        {
            var requestIdSet = request.RequestIds.ToHashSet();
            eligibleRequests = eligibleRequests.Where(r => requestIdSet.Contains(r.Id));
        }

        var spaces = await _spaceRepository.GetAllAsync(request.SiteId, cancellationToken);
        var capabilitiesBySpace = (await _capabilityRepository.GetByResourcesAsync(
                spaces.Select(s => s.Id).ToList(), cancellationToken))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.CriterionId).ToHashSet());
        var spaceNodes = spaces
            .Select(s => new SpaceNode(s.Id, s.Name, capabilitiesBySpace.GetValueOrDefault(s.Id) ?? []))
            .ToList();

        var spaceResourceIds = spaceNodes.Select(s => s.ResourceId).ToList();
        var blockedPeriodsByResource = await _resolver.GetBlockedPeriodsForResourcesAsync(
            request.SiteId, spaceResourceIds, cancellationToken);

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

        // Fixed occupancies: scheduled requests in this site whose bar can touch the horizon.
        // The solvers only consult occupancies on this site's spaces within the horizon, so the
        // site+window fetch is solver-equivalent to the previous tenant-wide scan. The upper bound
        // is exclusive-day so an assignment starting late on the last horizon day is still seen.
        // No scheduling_settings_apply filter — manually scheduled requests occupy spaces too.
        var scheduled = await _requestRepository.GetScheduledBySiteWindowAsync(
            request.SiteId,
            request.HorizonStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            request.HorizonEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            cancellationToken);
        var fixedAssignments = scheduled
            .Where(r => r.IsScheduled)
            .Select(r => new FixedOccupancy(
                r.Id,
                r.GetSpaceResourceId()!.Value,
                DateOnly.FromDateTime(r.StartTs!.Value),
                DateOnly.FromDateTime(r.EndTs!.Value)))
            .ToList();

        return new SchedulingProblem(
            request.SiteId, request.HorizonStart, request.HorizonEnd,
            requestNodes, spaceNodes, fixedAssignments,
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

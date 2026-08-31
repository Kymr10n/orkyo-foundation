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
    private readonly IRequestDependencyRepository _dependencyRepository;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        IResourceRepository resourceRepository,
        IResourceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository,
        IAvailabilityResolver resolver,
        IRequestDependencyRepository dependencyRepository)
    {
        _requestRepository = requestRepository;
        _resourceRepository = resourceRepository;
        _capabilityRepository = capabilityRepository;
        _schedulingRepository = schedulingRepository;
        _dependencyRepository = dependencyRepository;
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
                // Inclusive last day, not the raw end date: end_ts is half-open, and the
                // analyzer's overlap check treats occupancy End inclusively. The raw date of a
                // midnight end would phantom-occupy one extra day per applied placement.
                SchedulingEngine.InclusiveLastDay(x.Request.EndTs!.Value)))
            .ToList();

        // Precedence edges pointing at anything this run might place. One read for the whole
        // solve set — asking per request would be an N+1 over the backlog.
        var solveSet = requestNodes.Select(n => n.RequestId).ToHashSet();
        var edges = solveSet.Count == 0
            ? []
            : await _dependencyRepository.GetBySuccessorsAsync(solveSet, cancellationToken);

        // A predecessor already placed is a fixed date, not a variable: fold it into the
        // successor's earliest start rather than handing the solver a constraint over something
        // it cannot move. Requires an end date, which is what "placed" means here.
        //
        // `scheduled` only covers this site and this horizon, so it misses the commonest case of
        // all: a predecessor that finished last month. Resolving those separately is what stops
        // the run from refusing to place work whose prerequisite is already done.
        var placedEnds = scheduled
            .Where(r => r.EndTs.HasValue)
            // Inclusive last day: the fold-in below starts successors "the day after the
            // predecessor ends", so a raw midnight-exclusive date would cost every successor of
            // an applied placement one extra idle day.
            .ToDictionary(r => r.Id, r => SchedulingEngine.InclusiveLastDay(r.EndTs!.Value));

        var unresolved = edges
            .Select(e => e.PredecessorRequestId)
            .Where(id => !solveSet.Contains(id) && !placedEnds.ContainsKey(id))
            .Distinct()
            .ToList();

        if (unresolved.Count > 0)
            foreach (var predecessor in await _requestRepository.GetByIdsAsync(
                         unresolved, includeRequirements: false, cancellationToken))
                if (predecessor.EndTs is { } end)
                    placedEnds[predecessor.Id] = SchedulingEngine.InclusiveLastDay(end);

        var solverEdges = new List<DependencyEdge>();
        var earliestFromPredecessor = new Dictionary<Guid, DateOnly>();
        var blockedBySet = new HashSet<Guid>();

        foreach (var edge in edges)
        {
            var lagDays = LagToDays(edge.LagMinutes, settings);

            if (solveSet.Contains(edge.PredecessorRequestId))
            {
                // Both ends move together in this run: a real constraint for the solver.
                solverEdges.Add(new DependencyEdge(
                    edge.PredecessorRequestId, edge.SuccessorRequestId, lagDays));
            }
            else if (placedEnds.TryGetValue(edge.PredecessorRequestId, out var predEnd))
            {
                // Finish-to-start: the successor may start the day after the predecessor ends,
                // plus lag. Keep the latest such bound when several predecessors are placed.
                var bound = predEnd.AddDays(1 + lagDays);
                if (!earliestFromPredecessor.TryGetValue(edge.SuccessorRequestId, out var existing)
                    || bound > existing)
                    earliestFromPredecessor[edge.SuccessorRequestId] = bound;
            }
            else
            {
                // Genuinely unplaceable: not in this run, and with no end date to bound against.
                // Scheduling the successor would knowingly create a violation, so it stays in
                // the backlog with a reason.
                blockedBySet.Add(edge.SuccessorRequestId);
            }
        }

        // A folded bound that leaves no room is a dependency problem, not a capacity one.
        // Setting the impossible window anyway makes the feasibility analyzer drop every
        // candidate and report "no feasible start day", sending the planner to look at
        // resource load for something the predecessor's finish date caused.
        var nodesById = requestNodes.ToDictionary(n => n.RequestId);
        foreach (var (requestId, bound) in earliestFromPredecessor)
        {
            if (!nodesById.TryGetValue(requestId, out var node)) continue;

            var lastStart = (node.LatestEnd ?? request.HorizonEnd).AddDays(-(node.DurationDays - 1));
            if (bound > lastStart) blockedBySet.Add(requestId);
        }

        // Blocking travels downstream. If S cannot be placed then anything waiting on S cannot
        // either — and dropping the S→T edge without dropping T would leave T scheduled with
        // nothing holding it back, which is the violation this whole branch exists to avoid.
        if (blockedBySet.Count > 0)
        {
            var successorsOf = solverEdges
                .GroupBy(e => e.PredecessorRequestId)
                .ToDictionary(g => g.Key, g => g.Select(e => e.SuccessorRequestId).ToList());

            var pending = new Queue<Guid>(blockedBySet);
            while (pending.Count > 0)
            {
                if (!successorsOf.TryGetValue(pending.Dequeue(), out var downstream)) continue;
                foreach (var successor in downstream)
                    if (blockedBySet.Add(successor)) pending.Enqueue(successor);
            }
        }

        // Withheld requests leave the solve set, so no solver can report them. Carry them out
        // separately, with their names, or the caller sees a run that quietly returned fewer
        // requests than it was given and no reason for any of them.
        var withheld = blockedBySet.Count == 0
            ? []
            : requestNodes
                .Where(n => blockedBySet.Contains(n.RequestId))
                .Select(n => new WithheldRequestNode(n.RequestId, n.DisplayName))
                .ToList();

        if (earliestFromPredecessor.Count > 0 || blockedBySet.Count > 0)
        {
            requestNodes = requestNodes
                .Where(n => !blockedBySet.Contains(n.RequestId))
                .Select(n => earliestFromPredecessor.TryGetValue(n.RequestId, out var bound)
                    && (n.EarliestStart is null || bound > n.EarliestStart.Value)
                        ? n with { EarliestStart = bound }
                        : n)
                .ToList();

            // Edges with an endpoint outside the solve set have nothing left to constrain.
            solverEdges.RemoveAll(e => blockedBySet.Contains(e.SuccessorRequestId)
                                    || blockedBySet.Contains(e.PredecessorRequestId));
        }

        return new SchedulingProblem(
            request.SiteId, request.HorizonStart, request.HorizonEnd,
            requestNodes, resourceNodes, fixedAssignments,
            settings, blockedPeriodsByResource, solverEdges, withheld);
    }

    /// <summary>
    /// Lag in whole days, ceilinged. Rounding down would let a successor start before the gap
    /// the user asked for has elapsed; a lag can only ever push work later.
    /// </summary>
    private static int LagToDays(int lagMinutes, SchedulingSettingsInfo? settings)
    {
        if (lagMinutes <= 0) return 0;
        var minutesPerDay = MinutesPerDay(settings);
        return (int)Math.Ceiling(lagMinutes / (double)minutesPerDay);
    }

    /// <summary>
    /// How many minutes one planning day holds — the whole day, or the working window when
    /// working hours are on. Shared by duration and lag conversion so the two can never
    /// disagree about how long a day is.
    /// </summary>
    private static int MinutesPerDay(SchedulingSettingsInfo? settings)
    {
        if (settings is not { WorkingHoursEnabled: true }) return 24 * 60;

        // Compare before subtracting: TimeOnly subtraction is elapsed time and wraps at
        // midnight, so 09:00 - 17:00 is 16 hours, not -8. Subtracting first would let an
        // end-before-start setting through as a plausible-looking positive day length.
        if (settings.WorkingDayEnd <= settings.WorkingDayStart)
        {
            // SchedulingValidators rejects end <= start at the boundary, so this is
            // corrupt stored data — fail rather than silently invent an 8-hour day.
            throw new InvalidOperationException(
                $"Working hours are enabled but WorkingDayEnd ({settings.WorkingDayEnd}) "
                + $"is not after WorkingDayStart ({settings.WorkingDayStart}).");
        }
        return (int)(settings.WorkingDayEnd - settings.WorkingDayStart).TotalMinutes;
    }

    private static int DurationToDays(int value, DurationUnit unit, SchedulingSettingsInfo? settings)
    {
        var totalMinutes = SchedulingEngine.DurationToMinutes(value, unit);
        return Math.Max(1, (int)Math.Ceiling((double)totalMinutes / MinutesPerDay(settings)));
    }
}

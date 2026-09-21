using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services.AutoSchedule;

public class SchedulingProblemBuilder
{
    private readonly IRequestRepository _requestRepository;
    private readonly IRequestScheduleReadRepository _scheduleReads;
    private readonly IResourceRepository _resourceRepository;
    private readonly IResourceCapabilityRepository _capabilityRepository;
    private readonly ISchedulingRepository _schedulingRepository;
    private readonly IAvailabilityResolver _resolver;
    private readonly IRequestDependencyRepository _dependencyRepository;
    private readonly TimeProvider _time;

    public SchedulingProblemBuilder(
        IRequestRepository requestRepository,
        IRequestScheduleReadRepository scheduleReads,
        IResourceRepository resourceRepository,
        IResourceCapabilityRepository capabilityRepository,
        ISchedulingRepository schedulingRepository,
        IAvailabilityResolver resolver,
        IRequestDependencyRepository dependencyRepository,
        TimeProvider time)
    {
        _requestRepository = requestRepository;
        _scheduleReads = scheduleReads;
        _resourceRepository = resourceRepository;
        _capabilityRepository = capabilityRepository;
        _schedulingRepository = schedulingRepository;
        _dependencyRepository = dependencyRepository;
        _resolver = resolver;
        _time = time;
    }

    public virtual async Task<SchedulingProblem> BuildAsync(
        AutoSchedulePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var settings = await _schedulingRepository.GetSettingsAsync(request.SiteId, cancellationToken);

        // Everything the solver sees is in working minutes on this axis: durations, lags,
        // windows, occupancies. Non-working time does not exist on it, so a job spans nights
        // on its own and a twenty-minute operation costs twenty minutes, not a day.
        var axis = WorkingTimeAxis.Build(
            request.HorizonStart, request.HorizonEnd, settings, request.RespectSchedulingSettings);

        // The schedulable backlog is every leaf that isn't fully scheduled, in two disjoint fetches
        // that together reproduce the old tenant-wide `!IsScheduled` leaf filter without the heavy
        // GetAllAsync (which pulled every request, groups and finished ones included):
        //   • GetUnscheduledAsync — leaves with start_ts IS NULL (the drag-to-schedule backlog).
        //   • GetPartiallyScheduledLeavesAsync — leaves WITH a start_ts but still !IsScheduled
        //     (no end_ts, or a target type with no assignment). These are excluded from both the
        //     unscheduled backlog and the fixed-occupancy fetch, so without this second set
        //     they'd be invisible to the solver despite being auto-schedulable before.
        var unscheduled = await _scheduleReads.GetUnscheduledAsync(
            includeRequirements: true, ct: cancellationToken);
        var partiallyScheduled = await _scheduleReads.GetPartiallyScheduledLeavesAsync(
            includeRequirements: true, ct: cancellationToken);

        // The types the run fills are resolved by AutoScheduleService before this is called; an
        // empty set here means a caller skipped that resolution, and guessing would hide it.
        var runTypes = request.ResourceTypeKeys is { Count: > 0 } keys
            ? keys.ToHashSet(StringComparer.Ordinal)
            : throw new ArgumentException(
                "ResourceTypeKeys must be resolved before building the problem.", nameof(request));

        var horizonFrom = request.HorizonStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var horizonTo = request.HorizonEnd.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        // A request is in the run when it wants at least one of the run's types and has not
        // already got a resource of it. Without the second test a request whose room is already
        // booked would be offered another one. A request with a window already chosen is in the
        // run only when that window lies inside the horizon: its window is pinned (below), and a
        // window from last month pinned onto this horizon's axis would be dragged into it.
        var eligibleRequests = unscheduled
            .Concat(partiallyScheduled)
            .Where(r => r.Status is RequestStatus.New or RequestStatus.InProgress)
            .Where(r => r.MinimalDurationValue > 0)
            .Where(r => OpenTypes(r, runTypes).Count > 0)
            .Where(r => r.StartTs is null || r.EndTs is null
                        || (r.StartTs >= horizonFrom && r.EndTs <= horizonTo))
            .ToList();

        if (request.RequestIds is { Count: > 0 })
        {
            var requestIdSet = request.RequestIds.ToHashSet();
            eligibleRequests = eligibleRequests.Where(r => requestIdSet.Contains(r.Id)).ToList();
        }

        // Window the site filter to the horizon. Without it the filter resolves a travelling
        // resource's location as of now(), so solving three months out included or excluded
        // people and tools by where they happen to be today — and the same run tomorrow
        // produced a different pool, and a different fingerprint.
        // Every candidate: a pool silently cut at 1000 would make the solver produce a valid
        // schedule over the wrong set, and change the run fingerprint for no visible reason.
        // One read per type, in key order so the pool — and the fingerprint — is stable.
        var candidates = new List<ResourceInfo>();
        foreach (var typeKey in runTypes.Order(StringComparer.Ordinal))
        {
            candidates.AddRange(await _resourceRepository.GetEveryAsync(
                new ResourceListFilter
                {
                    ResourceTypeKey = typeKey,
                    SiteId = request.SiteId,
                    SiteWindowFrom = horizonFrom,
                    SiteWindowTo = horizonTo,
                    IsActive = true,
                },
                cancellationToken));
        }
        var capabilitiesByResource = (await _capabilityRepository.GetByResourcesAsync(
                candidates.Select(c => c.Id).ToList(), cancellationToken))
            .GroupBy(c => c.ResourceId)
            .ToDictionary(g => g.Key, g => g.Select(c => c.CriterionId).ToHashSet());
        var resourceNodes = candidates
            .Select(c => new ResourceNode(
                c.Id, c.Name, c.ResourceTypeKey, capabilitiesByResource.GetValueOrDefault(c.Id) ?? []))
            .ToList();

        // A criterion is scoped to resource types, so a requirement written for the mill must
        // not be demanded of the van the same request also needs. The scope rides on each
        // loaded requirement (the same projection the assignment validator decides from), so
        // no second read of the criteria table is needed.
        var criterionTypeScopes = eligibleRequests
            .SelectMany(r => r.Requirements ?? [])
            .Where(q => q.Criterion is not null)
            .GroupBy(q => q.CriterionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlySet<string>)g.First().Criterion!.ResourceTypeKeys.ToHashSet(StringComparer.Ordinal));

        var candidateIds = resourceNodes.Select(n => n.ResourceId).ToList();
        var blockedPeriodsByResource = await _resolver.GetBlockedPeriodsForResourcesAsync(
            request.SiteId, candidateIds, cancellationToken);

        var requestNodes = new List<RequestNode>();
        foreach (var r in eligibleRequests)
        {
            int? earliest, latest;
            int duration;
            if (r.StartTs is { } pinnedStart && r.EndTs is { } pinnedEnd)
            {
                // A window someone already chose is a fact about the plan, not an estimate:
                // the run fills the open types at exactly that window. Moving it would also
                // move the resources already booked on it, past whatever else they hold.
                var start = axis.ToOffset(pinnedStart);
                var end = Math.Max(start + 1, axis.ToOffsetEnd(pinnedEnd));
                earliest = start;
                latest = end;
                duration = end - start;
            }
            else
            {
                earliest = r.EarliestStartTs is { } earliestTs ? axis.ToOffset(earliestTs) : null;
                latest = r.LatestEndTs is { } latestTs ? axis.ToOffset(latestTs) : null;
                // At least a minute: a zero-length placement would have no window to write.
                duration = Math.Max(1, SchedulingEngine.DurationToMinutes(r.MinimalDurationValue, r.MinimalDurationUnit));
            }

            requestNodes.Add(new RequestNode(
                r.Id,
                r.Name,
                earliest,
                latest,
                duration,
                Priority: (int)r.Status,
                r.Requirements?.Select(req => req.CriterionId).ToHashSet() ?? new HashSet<Guid>(),
                OpenTypes(r, runTypes)));
        }

        // Fixed occupancies: requests in this site whose bar can touch the horizon. The solvers
        // only consult occupancies on the candidate resources within the horizon, so the
        // site+window fetch is solver-equivalent to the previous tenant-wide scan. The upper bound
        // is exclusive-day so an assignment starting late on the last horizon day is still seen.
        // No scheduling_settings_apply filter — manually scheduled requests occupy resources too.
        var scheduled = await _scheduleReads.GetScheduledBySiteWindowAsync(
            request.SiteId, horizonFrom, horizonTo, cancellationToken);
        // Holding a resource is what occupies it — not being fully scheduled. A request still
        // waiting on its technician has its room booked all the same, and offering that room to
        // someone else would double-book it. Every booking on a pool resource counts, including
        // those of requests in this run: their booked types are not open, so the booking blocks
        // others without constraining them.
        // Kept even when the window collapses to a point (a hand-made Saturday booking on a
        // weekday-only axis): a placement may touch that point but must not span it, because
        // the booking still covers the calendar weekend the axis compressed away.
        var poolIds = resourceNodes.Select(n => n.ResourceId).ToHashSet();
        var fixedAssignments = scheduled
            .SelectMany(r => r.Assignments)
            .Where(a => poolIds.Contains(a.ResourceId))
            .Select(a => new FixedOccupancy(
                a.RequestId,
                a.ResourceId,
                axis.ToOffset(a.StartUtc),
                axis.ToOffsetEnd(a.EndUtc)))
            .ToList();

        // A resource's own blocked periods (absences, maintenance) occupy it like a booking
        // does. One that lies entirely in non-working time costs no working capacity, so it is
        // dropped rather than kept as a point: nothing can be scheduled across it anyway.
        foreach (var (resourceId, periods) in blockedPeriodsByResource)
        {
            foreach (var period in periods)
            {
                var start = axis.ToOffset(period.StartTs);
                var end = axis.ToOffsetEnd(period.EndTs);
                if (end > start)
                    fixedAssignments.Add(new FixedOccupancy(Guid.Empty, resourceId, start, end));
            }
        }

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
            .ToDictionary(r => r.Id, r => r.EndTs!.Value);

        var unresolved = edges
            .Select(e => e.PredecessorRequestId)
            .Where(id => !solveSet.Contains(id) && !placedEnds.ContainsKey(id))
            .Distinct()
            .ToList();

        // Predecessor state, for the abandonment test below. `scheduled` covers this site and
        // horizon; the unresolved read covers everything else an edge points at.
        var predecessorState = scheduled.ToDictionary(r => r.Id);

        if (unresolved.Count > 0)
            foreach (var predecessor in await _requestRepository.GetByIdsAsync(
                         unresolved, includeRequirements: false, cancellationToken))
            {
                predecessorState[predecessor.Id] = predecessor;
                if (predecessor.EndTs is { } end)
                    placedEnds[predecessor.Id] = end;
            }

        var solverEdges = new List<DependencyEdge>();
        var earliestFromPredecessor = new Dictionary<Guid, int>();
        var blockedBySet = new HashSet<Guid>();
        var joinConditions = new Dictionary<Guid, JoinCondition>();
        var nodesById = requestNodes.ToDictionary(n => n.RequestId);

        // What each successor still needs, so blocking can be resolved by the join rather than by
        // reachability. Only successors that depend on the run's own placements are listed: one
        // satisfied purely by immovable work cannot be un-satisfied by anything the run does.
        var joinNeeds = new Dictionary<Guid, (int Required, int FromPlaced, List<Guid> SolverPredecessors)>();

        // Triage runs per SUCCESSOR, not per edge: a join condition is a property of a request's
        // whole incoming set ("2 of my 3"), so no single edge can be classified on its own.
        var eligibleById = eligibleRequests.ToDictionary(r => r.Id);
        var now = _time.GetUtcNow().UtcDateTime;

        foreach (var incoming in edges.GroupBy(e => e.SuccessorRequestId))
        {
            var successorId = incoming.Key;
            var condition = eligibleById.TryGetValue(successorId, out var successor)
                ? JoinCondition.Of(successor)
                : JoinCondition.All;
            joinConditions[successorId] = condition;

            // A cancelled or deferred predecessor is work that will not happen. Dropping it
            // shrinks n, so an "all" join is not held shut forever by something abandoned.
            var live = incoming
                .Where(e => !(predecessorState.TryGetValue(e.PredecessorRequestId, out var p)
                              && JoinConditionEvaluator.IsAbandoned(p, now)))
                .ToList();

            var placedBounds = new List<int>();
            var edgesForSolver = new List<DependencyEdge>();

            foreach (var edge in live)
            {
                if (solveSet.Contains(edge.PredecessorRequestId))
                {
                    // Both ends move together in this run: a candidate constraint for the solver.
                    edgesForSolver.Add(new DependencyEdge(edge.PredecessorRequestId, successorId, edge.LagMinutes));
                }
                else if (placedEnds.TryGetValue(edge.PredecessorRequestId, out var predEnd))
                {
                    // Finish-to-start: the successor may start the minute the predecessor
                    // ends, plus lag. The lag is elapsed time here — a predecessor that
                    // finished last month has long served it — so it is added before the
                    // instant is put on the axis, where anything before the horizon is 0.
                    placedBounds.Add(axis.ToOffsetEnd(predEnd.AddMinutes(edge.LagMinutes)));
                }
                // else: not in this run and with no end date to bound against — it can satisfy
                // nothing, so it simply does not count towards the condition below.
            }

            var required = JoinConditionEvaluator.RequiredCount(condition, live.Count);

            if (required == 0)
            {
                // Nothing left to wait for: every predecessor was abandoned, or there are none.
                continue;
            }

            // The last minute this successor could still start. A bound past it is a dependency
            // problem, not a capacity one — the feasibility analyzer would otherwise drop every
            // candidate and report "no feasible start", sending the planner to look at
            // resource load for something a predecessor's finish caused.
            var lastStart = nodesById.TryGetValue(successorId, out var node)
                ? (node.LatestEnd ?? axis.Length) - node.DurationMinutes
                : (int?)null;

            var placedFold = JoinConditionEvaluator.FoldEarliestStart(condition, placedBounds);
            var placedAlone = placedBounds.Count >= required
                && (placedFold is not { } f || lastStart is not { } last || f <= last);

            if (placedAlone)
            {
                // Satisfiable from work that is placed and immovable, WITHOUT pushing the
                // successor out of its own window. Hand the solver no edges: the condition is
                // already met, and constraining it against co-scheduled predecessors it does not
                // need would delay it for nothing. Under "all" this is the familiar case where
                // every predecessor is already placed and the fold is the max, exactly as before.
                if (placedFold is { } bound) earliestFromPredecessor[successorId] = bound;
            }
            else if (placedBounds.Count + edgesForSolver.Count >= required)
            {
                // Satisfiable only with help from predecessors this run is placing — or placed
                // work alone would push it past its own deadline, and a co-scheduled predecessor
                // can do better. The solvers fold edges with a max, so the join is kept
                // conservatively: every solver edge is handed over.
                solverEdges.AddRange(edgesForSolver);
                joinNeeds[successorId] = (
                    required,
                    placedBounds.Count,
                    [.. edgesForSolver.Select(e => e.PredecessorRequestId)]);

                // Bind only as many placed bounds as the solver edges cannot cover. Max-folding
                // all of them would re-impose the far-future bound this branch exists to escape:
                // for "any" with one distant placed predecessor and one co-scheduled, nothing
                // from the placed side is needed at all.
                var neededFromPlaced = Math.Max(0, required - edgesForSolver.Count);
                if (neededFromPlaced > 0 && placedBounds.Count > 0)
                {
                    var ordered = placedBounds.Order().ToList();
                    earliestFromPredecessor[successorId] = ordered[Math.Min(neededFromPlaced, ordered.Count) - 1];
                }
            }
            else
            {
                // Not satisfiable at all this run: too few predecessors are placed or placeable.
                // Scheduling the successor would knowingly create a violation, so it stays in
                // the backlog with a reason.
                blockedBySet.Add(successorId);
            }
        }

        // A bound that still leaves no room blocks the successor outright. (The triage above
        // already preferred the solver where one could do better, so reaching here means nothing
        // in this run can place it inside its window.)
        foreach (var (requestId, bound) in earliestFromPredecessor)
        {
            if (!nodesById.TryGetValue(requestId, out var node)) continue;

            var lastStart = (node.LatestEnd ?? axis.Length) - node.DurationMinutes;
            if (bound > lastStart) blockedBySet.Add(requestId);
        }

        // Blocking travels downstream, but it travels through the JOIN, not through plain
        // reachability. If S cannot be placed, a successor that needs "all" of its predecessors
        // is finished — while one that needs "any" is perfectly fine as long as another
        // predecessor survives. Walking the edges alone would re-impose "all" on every join and
        // undo the whole triage above.
        //
        // A fixpoint rather than a queue: blocking a request can drop a later successor below its
        // requirement, which can drop another, and the dependency order is not known here.
        if (blockedBySet.Count > 0)
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var (successorId, need) in joinNeeds)
                {
                    if (blockedBySet.Contains(successorId)) continue;

                    var available = need.FromPlaced
                        + need.SolverPredecessors.Count(id => !blockedBySet.Contains(id));

                    if (available < need.Required && blockedBySet.Add(successorId)) changed = true;
                }
            } while (changed);
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
            request.SiteId, request.HorizonStart, request.HorizonEnd, axis,
            requestNodes, resourceNodes, fixedAssignments,
            solverEdges, withheld, joinConditions, criterionTypeScopes);
    }

    /// <summary>The run's types this request targets and has no resource of yet.</summary>
    private static IReadOnlySet<string> OpenTypes(RequestInfo r, IReadOnlySet<string> runTypes)
        => r.TargetResourceTypeKeys
            .Where(t => runTypes.Contains(t) && r.GetResourceIdForType(t) is null)
            .ToHashSet(StringComparer.Ordinal);
}

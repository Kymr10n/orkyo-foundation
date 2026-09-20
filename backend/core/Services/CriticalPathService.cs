using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Critical path over the request dependency network.
/// </summary>
public interface ICriticalPathService
{
    /// <summary>
    /// Computes earliest/latest instants and float for every request that takes part in a
    /// dependency, optionally scoped to a site. Throws <see cref="ConflictException"/> when the
    /// graph contains a cycle, because a cycle has no forward pass.
    /// </summary>
    Task<CriticalPathResult> ComputeAsync(Guid? siteId, CancellationToken ct = default);
}

/// <summary>
/// Classic CPM — a forward pass for earliest instants, a backward pass for latest instants,
/// float as the difference — over the leaves that carry dependency edges.
///
/// Two things make this Orkyo's version rather than a textbook one:
///
/// A scheduled request is an anchor, not an estimate. When work is already placed, its dates are
/// facts about the plan, and the pass takes them as given rather than proposing something
/// earlier. Only unscheduled work floats to where its predecessors allow.
///
/// Everything is in calendar minutes. Minutes because that is the resolution the scheduler
/// plans in; calendar rather than one site's working hours because the network can span sites
/// that keep different hours, and a lag is elapsed time.
/// </summary>
public class CriticalPathService : ICriticalPathService
{
    private readonly IRequestDependencyRepository _dependencies;
    private readonly IRequestRepository _requests;
    private readonly TimeProvider _time;

    public CriticalPathService(
        IRequestDependencyRepository dependencies,
        IRequestRepository requests,
        TimeProvider time)
    {
        _dependencies = dependencies;
        _requests = requests;
        _time = time;
    }

    public async Task<CriticalPathResult> ComputeAsync(Guid? siteId, CancellationToken ct = default)
    {
        var edges = await _dependencies.GetAllAsync(siteId, ct);
        if (edges.Count == 0)
            return Empty();

        var ids = edges
            .SelectMany(e => new[] { e.PredecessorRequestId, e.SuccessorRequestId })
            .Distinct()
            .ToList();

        var requests = (await _requests.GetByIdsAsync(ids, includeRequirements: false, ct))
            .ToDictionary(r => r.Id);

        // An edge can outlive the visibility of its endpoint — a site filter follows the
        // successor, so a predecessor at another site is not in this read. Drop those edges
        // rather than inventing dates for a request we cannot see.
        var diagnostics = new List<string>();
        var usable = edges
            .Where(e => requests.ContainsKey(e.PredecessorRequestId) && requests.ContainsKey(e.SuccessorRequestId))
            .ToList();

        if (usable.Count != edges.Count)
            diagnostics.Add($"{edges.Count - usable.Count} dependency edge(s) reference requests outside this scope and were excluded.");

        // A cancelled or deferred predecessor is work that will not happen, so it constrains
        // nothing. Dropping its edges here is the same rule the execution gate applies, and it
        // keeps an "all" join from waiting forever on something abandoned.
        var now = _time.GetUtcNow().UtcDateTime;
        var abandoned = usable
            .Where(e => JoinConditionEvaluator.IsAbandoned(requests[e.PredecessorRequestId], now))
            .ToList();
        if (abandoned.Count > 0)
        {
            usable = usable.Except(abandoned).ToList();
            diagnostics.Add($"{abandoned.Count} dependency edge(s) start at a cancelled or deferred request and were excluded.");
        }

        if (usable.Count == 0)
            return Empty(diagnostics);

        var nodeIds = usable
            .SelectMany(e => new[] { e.PredecessorRequestId, e.SuccessorRequestId })
            .Distinct()
            .ToList();

        var order = TopologicalOrder(nodeIds, usable)
            ?? throw new ConflictException(
                "The dependency graph contains a cycle, so a critical path cannot be computed.");

        var successorsOf = usable.GroupBy(e => e.PredecessorRequestId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var predecessorsOf = usable.GroupBy(e => e.SuccessorRequestId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var duration = nodeIds.ToDictionary(id => id, id => DurationMinutes(requests[id]));

        // ── Forward pass ────────────────────────────────────────────────────────
        var earliestStart = new Dictionary<Guid, DateTime>();
        var earliestFinish = new Dictionary<Guid, DateTime>();

        // Unanchored work has to start somewhere; the network's own earliest known instant is
        // the honest floor — it keeps the numbers relative to the plan rather than to "now",
        // which would make the same graph report differently on different days.
        var floor = nodeIds
            .Select(id => Anchor(requests[id]))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(ThisMinute(_time.GetUtcNow().UtcDateTime))
            .Min();

        // Which incoming edges actually held a request back. Under "all" that is all of them, so
        // the backward pass below is unchanged; under "any" and k-of-n the slack branches are
        // excluded, because a request the join never waited for cannot constrain how late that
        // predecessor may finish.
        var bindingSuccessorsOf = new Dictionary<Guid, List<RequestDependencyInfo>>();

        foreach (var id in order)
        {
            var start = Anchor(requests[id]) ?? floor;

            if (predecessorsOf.TryGetValue(id, out var incoming))
            {
                // One candidate start per predecessor, folded by the request's join condition:
                // the latest for "all", the earliest for "any", the k-th earliest for k_of_n.
                var bounds = incoming
                    .Select(edge => earliestFinish[edge.PredecessorRequestId].AddMinutes(edge.LagMinutes))
                    .ToList();

                var condition = JoinCondition.Of(requests[id]);
                if (JoinConditionEvaluator.FoldEarliestStart(condition, bounds) is { } bound)
                {
                    if (bound > start) start = bound;

                    // Binding = the predecessors the fold actually selected. For "all" every edge
                    // qualifies (the fold is the max, and every bound is <= it). For "any" only
                    // the earliest does; for k-of-n, the k earliest.
                    for (var i = 0; i < incoming.Count; i++)
                        if (bounds[i] <= bound)
                        {
                            if (!bindingSuccessorsOf.TryGetValue(incoming[i].PredecessorRequestId, out var list))
                                bindingSuccessorsOf[incoming[i].PredecessorRequestId] = list = [];
                            list.Add(incoming[i]);
                        }
                }
            }

            earliestStart[id] = start;
            earliestFinish[id] = start.AddMinutes(duration[id]);
        }

        var projectFinish = earliestFinish.Values.Max();

        if (nodeIds.Any(id => predecessorsOf.ContainsKey(id)
                && requests[id].PredecessorLogic != PredecessorLogic.All))
            diagnostics.Add(
                "Some requests start on \"any\" or k-of-n of their predecessors, so a predecessor "
                + "the join did not wait for carries float rather than lying on the critical path.");

        // ── Backward pass ───────────────────────────────────────────────────────
        var latestFinish = new Dictionary<Guid, DateTime>();
        var latestStart = new Dictionary<Guid, DateTime>();

        // The backward pass folds against the BINDING successors only — the ones whose join
        // actually waited for this request. Folding against every successor would treat an "any"
        // join as an "all" and pull the slack branch's latest finish back to the critical one's,
        // producing negative float and marking a request critical that nothing waits for.
        foreach (var id in Enumerable.Reverse(order))
        {
            var finish = projectFinish;

            if (bindingSuccessorsOf.TryGetValue(id, out var outgoing))
                foreach (var edge in outgoing)
                {
                    var bound = latestStart[edge.SuccessorRequestId].AddMinutes(-edge.LagMinutes);
                    if (bound < finish) finish = bound;
                }

            // A deadline of its own can only tighten the answer, never loosen it.
            if (requests[id].LatestEndTs is { } deadline && deadline < finish)
                finish = deadline;

            latestFinish[id] = finish;
            latestStart[id] = finish.AddMinutes(-duration[id]);
        }

        var nodes = nodeIds
            .Select(id =>
            {
                var floatMinutes = (int)(latestStart[id] - earliestStart[id]).TotalMinutes;
                return new CriticalPathNode
                {
                    RequestId = id,
                    Name = requests[id].Name,
                    EarliestStart = earliestStart[id],
                    EarliestFinish = earliestFinish[id],
                    LatestStart = latestStart[id],
                    LatestFinish = latestFinish[id],
                    TotalFloatMinutes = floatMinutes,
                    IsCritical = floatMinutes <= 0,
                    IsScheduled = Anchor(requests[id]).HasValue,
                };
            })
            .OrderBy(n => n.EarliestStart)
            .ThenBy(n => n.Name, StringComparer.Ordinal)
            // Names repeat — the seeder alone makes hundreds of same-named jobs — so without a
            // unique final key the order depends on edge-read order and shifts between calls.
            .ThenBy(n => n.RequestId)
            .ToList();

        var networkStart = earliestStart.Values.Min();

        return new CriticalPathResult
        {
            Nodes = nodes,
            Edges = usable,
            DurationMinutes = (int)(projectFinish - networkStart).TotalMinutes,
            Diagnostics = diagnostics,
        };
    }

    /// <summary>
    /// No usable network. Reports no edges rather than the ones that were read: a caller drawing
    /// them would render links to requests that are not in <c>Nodes</c>.
    /// </summary>
    private static CriticalPathResult Empty(List<string>? diagnostics = null)
        => new()
        {
            Nodes = [],
            Edges = [],
            DurationMinutes = 0,
            Diagnostics = diagnostics ?? [],
        };

    /// <summary>The placed start when the request is scheduled, otherwise null.</summary>
    private static DateTime? Anchor(RequestInfo request) => request.StartTs;

    /// <summary>
    /// How many minutes the request occupies: its actual span when placed, otherwise its
    /// minimal duration. Always at least one — a zero-length node would make float meaningless.
    /// </summary>
    private static int DurationMinutes(RequestInfo request)
    {
        if (request.StartTs is { } start && request.EndTs is { } end)
            return Math.Max(1, (int)(end - start).TotalMinutes);

        return Math.Max(1, SchedulingEngine.DurationToMinutes(request.MinimalDurationValue, request.MinimalDurationUnit));
    }

    /// <summary>"Now" at minute precision, so an unanchored network reports stable numbers within a minute.</summary>
    private static DateTime ThisMinute(DateTime utc)
        => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc);

    /// <summary>Kahn's algorithm. Returns null when a cycle leaves nodes unresolvable.</summary>
    private static List<Guid>? TopologicalOrder(
        IReadOnlyList<Guid> nodeIds, IReadOnlyList<RequestDependencyInfo> edges)
    {
        var indegree = nodeIds.ToDictionary(id => id, _ => 0);
        var successors = new Dictionary<Guid, List<Guid>>();

        foreach (var edge in edges)
        {
            if (!successors.TryGetValue(edge.PredecessorRequestId, out var list))
                successors[edge.PredecessorRequestId] = list = [];
            list.Add(edge.SuccessorRequestId);
            indegree[edge.SuccessorRequestId]++;
        }

        var ready = new Queue<Guid>(nodeIds.Where(id => indegree[id] == 0));
        var order = new List<Guid>(nodeIds.Count);

        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            order.Add(id);

            if (!successors.TryGetValue(id, out var outgoing)) continue;
            foreach (var succ in outgoing)
                if (--indegree[succ] == 0) ready.Enqueue(succ);
        }

        return order.Count == nodeIds.Count ? order : null;
    }
}

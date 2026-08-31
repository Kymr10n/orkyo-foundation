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
    /// Computes earliest/latest dates and float for every request that takes part in a
    /// dependency, optionally scoped to a site. Throws <see cref="ConflictException"/> when the
    /// graph contains a cycle, because a cycle has no forward pass.
    /// </summary>
    Task<CriticalPathResult> ComputeAsync(Guid? siteId, CancellationToken ct = default);
}

/// <summary>
/// Classic CPM — a forward pass for earliest dates, a backward pass for latest dates, float as
/// the difference — over the leaves that carry dependency edges.
///
/// Two things make this Orkyo's version rather than a textbook one:
///
/// A scheduled request is an anchor, not an estimate. When work is already placed, its dates are
/// facts about the plan, and the pass takes them as given rather than proposing something
/// earlier. Only unscheduled work floats to where its predecessors allow.
///
/// Everything is in whole days, because that is the granularity the whole scheduler works in.
/// </summary>
public class CriticalPathService : ICriticalPathService
{
    private readonly IRequestDependencyRepository _dependencies;
    private readonly IRequestRepository _requests;

    public CriticalPathService(IRequestDependencyRepository dependencies, IRequestRepository requests)
    {
        _dependencies = dependencies;
        _requests = requests;
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

        var duration = nodeIds.ToDictionary(id => id, id => DurationDays(requests[id]));

        // ── Forward pass ────────────────────────────────────────────────────────
        var earliestStart = new Dictionary<Guid, DateOnly>();
        var earliestFinish = new Dictionary<Guid, DateOnly>();

        // Unanchored work has to start somewhere; the network's own earliest known date is the
        // honest floor — it keeps the numbers relative to the plan rather than to "today", which
        // would make the same graph report differently on different days.
        var floor = nodeIds
            .Select(id => Anchor(requests[id]))
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .DefaultIfEmpty(DateOnly.FromDateTime(DateTime.UtcNow))
            .Min();

        foreach (var id in order)
        {
            var start = Anchor(requests[id]) ?? floor;

            if (predecessorsOf.TryGetValue(id, out var incoming))
                foreach (var edge in incoming)
                {
                    var bound = earliestFinish[edge.PredecessorRequestId].AddDays(1 + LagDays(edge));
                    if (bound > start) start = bound;
                }

            earliestStart[id] = start;
            earliestFinish[id] = start.AddDays(duration[id] - 1);
        }

        var projectFinish = earliestFinish.Values.Max();

        // ── Backward pass ───────────────────────────────────────────────────────
        var latestFinish = new Dictionary<Guid, DateOnly>();
        var latestStart = new Dictionary<Guid, DateOnly>();

        foreach (var id in Enumerable.Reverse(order))
        {
            var finish = projectFinish;

            if (successorsOf.TryGetValue(id, out var outgoing))
                foreach (var edge in outgoing)
                {
                    var bound = latestStart[edge.SuccessorRequestId].AddDays(-(1 + LagDays(edge)));
                    if (bound < finish) finish = bound;
                }

            // A deadline of its own can only tighten the answer, never loosen it.
            if (requests[id].LatestEndTs is { } latest)
            {
                var deadline = DateOnly.FromDateTime(latest);
                if (deadline < finish) finish = deadline;
            }

            latestFinish[id] = finish;
            latestStart[id] = finish.AddDays(-(duration[id] - 1));
        }

        var nodes = nodeIds
            .Select(id =>
            {
                var floatDays = latestStart[id].DayNumber - earliestStart[id].DayNumber;
                return new CriticalPathNode
                {
                    RequestId = id,
                    Name = requests[id].Name,
                    EarliestStart = earliestStart[id],
                    EarliestFinish = earliestFinish[id],
                    LatestStart = latestStart[id],
                    LatestFinish = latestFinish[id],
                    TotalFloatDays = floatDays,
                    IsCritical = floatDays <= 0,
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
            DurationDays = projectFinish.DayNumber - networkStart.DayNumber + 1,
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
            DurationDays = 0,
            Diagnostics = diagnostics ?? [],
        };

    /// <summary>The placed start when the request is scheduled, otherwise null.</summary>
    private static DateOnly? Anchor(RequestInfo request)
        => request.StartTs.HasValue ? DateOnly.FromDateTime(request.StartTs.Value) : null;

    /// <summary>
    /// How many days the request occupies: its actual span when placed, otherwise its minimal
    /// duration. Always at least one — a zero-length node would make float meaningless.
    /// </summary>
    private static int DurationDays(RequestInfo request)
    {
        if (request.StartTs is { } start && request.EndTs is { } end)
            // Inclusive last day (end_ts is half-open): the raw date of a midnight end would
            // report every applied one-day placement as two days on the critical path.
            return Math.Max(1, SchedulingEngine.InclusiveLastDay(end).DayNumber - DateOnly.FromDateTime(start).DayNumber + 1);

        var minutes = SchedulingEngine.DurationToMinutes(request.MinimalDurationValue, request.MinimalDurationUnit);
        return Math.Max(1, (int)Math.Ceiling(minutes / (double)(24 * 60)));
    }

    /// <summary>
    /// Lag in whole days, ceilinged so it never lets a successor start early. Working hours are
    /// not applied here: the critical path spans calendar days across sites that can keep
    /// different hours, and one site's working day is not a property of the network.
    /// </summary>
    private static int LagDays(RequestDependencyInfo edge)
        => edge.LagMinutes <= 0 ? 0 : (int)Math.Ceiling(edge.LagMinutes / (double)(24 * 60));

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

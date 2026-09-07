using Api.Helpers;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Assembles the planner's view of one parent: its children, the dependencies among them, and
/// whether each child may start. A read model — it owns no rules of its own, deferring every
/// join-condition question to <see cref="JoinConditionEvaluator"/> so the picture the planner
/// draws cannot disagree with what the scheduler and the execution gate do.
/// </summary>
public interface IRequestPlanService
{
    /// <summary>The plan for <paramref name="parentId"/>, or null when no such request exists.</summary>
    Task<RequestPlan?> GetPlanAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>
    /// The plan across a whole site (or the tenant, when <paramref name="siteId"/> is null):
    /// every leaf task plus the edges among them — cross-group edges included, which the
    /// per-parent plan can only count. Site-neutral tasks are kept under every site.
    /// </summary>
    Task<SiteRequestPlan> GetSitePlanAsync(Guid? siteId, CancellationToken ct = default);
}

public class RequestPlanService(
    IRequestRepository requests,
    IRequestDependencyRepository dependencies) : IRequestPlanService
{
    public async Task<RequestPlan?> GetPlanAsync(Guid parentId, CancellationToken ct = default)
    {
        var parent = await requests.GetByIdAsync(parentId, includeRequirements: false, ct);
        if (parent is null) return null;

        var children = await requests.GetChildrenAsync(parentId, ct);
        if (children.Count == 0)
            return new RequestPlan
            {
                ParentId = parent.Id,
                ParentName = parent.Name,
                ParentPlanningMode = parent.PlanningMode,
                Children = [],
                Edges = [],
            };

        var (planChildren, internalEdges) = await AssembleAsync(children, ct);

        return new RequestPlan
        {
            ParentId = parent.Id,
            ParentName = parent.Name,
            ParentPlanningMode = parent.PlanningMode,
            Edges = internalEdges,
            Children = planChildren,
        };
    }

    public async Task<SiteRequestPlan> GetSitePlanAsync(Guid? siteId, CancellationToken ct = default)
    {
        // The site filter keeps site-neutral rows, the same rule the requests list applies.
        var leaves = (await requests.GetAllAsync(includeRequirements: false, siteId, ct))
            .Where(r => r.PlanningMode == PlanningMode.Leaf)
            .ToList();

        var (children, edges) = await AssembleAsync(leaves, ct);

        // The bands. A parent can sit at another site than its children (or be site-neutral),
        // so it is fetched by id rather than assumed to be in the leaf query's scope.
        var parentIds = leaves
            .Where(l => l.ParentRequestId.HasValue)
            .Select(l => l.ParentRequestId!.Value)
            .Distinct()
            .ToList();
        var groups = parentIds.Count == 0
            ? new List<SitePlanGroup>()
            : (await requests.GetByIdsAsync(parentIds, includeRequirements: false, ct))
                .Select(p => new SitePlanGroup { Id = p.Id, Name = p.Name, SortOrder = p.SortOrder })
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Name)
                .ToList();

        return new SiteRequestPlan { Groups = groups, Children = children, Edges = edges };
    }

    /// <summary>
    /// The shared assembly step over any node set: partition its touching edges into drawable
    /// (both ends inside) and external counts, and evaluate every node's join condition. The
    /// per-parent plan runs it over one group's children, the site plan over a site's leaves —
    /// one projection, so the two canvases cannot disagree on what a node says.
    /// </summary>
    private async Task<(List<RequestPlanChild> Children, List<RequestDependencyInfo> InternalEdges)> AssembleAsync(
        IReadOnlyList<RequestInfo> children, CancellationToken ct)
    {
        var childIds = children.Select(c => c.Id).ToHashSet();
        var touching = await dependencies.GetTouchingAsync(childIds, ct);

        // Both ends inside the set: the edges the planner can actually draw.
        var internalEdges = touching
            .Where(e => childIds.Contains(e.PredecessorRequestId) && childIds.Contains(e.SuccessorRequestId))
            .ToList();

        var externalPredecessors = touching
            .Where(e => childIds.Contains(e.SuccessorRequestId) && !childIds.Contains(e.PredecessorRequestId))
            .GroupBy(e => e.SuccessorRequestId)
            .ToDictionary(g => g.Key, g => g.Count());

        var externalSuccessors = touching
            .Where(e => childIds.Contains(e.PredecessorRequestId) && !childIds.Contains(e.SuccessorRequestId))
            .GroupBy(e => e.PredecessorRequestId)
            .ToDictionary(g => g.Key, g => g.Count());

        // CanStart weighs EVERY predecessor, inside the set or not — a task waiting on work in
        // another group is no more startable for the planner not being able to draw it.
        var predecessorsOf = touching
            .Where(e => childIds.Contains(e.SuccessorRequestId))
            .GroupBy(e => e.SuccessorRequestId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.PredecessorRequestId).ToList());

        var predecessorIds = predecessorsOf.Values.SelectMany(v => v).Distinct().ToList();
        var predecessorsById = predecessorIds.Count == 0
            ? []
            : (await requests.GetByIdsAsync(predecessorIds, includeRequirements: false, ct))
                .ToDictionary(r => r.Id);

        // Stored, not derived — the same reason the execution gate reads them: a predecessor
        // finished without ever being scheduled must not padlock its successors here either.
        var storedStatuses = await requests.GetStoredStatusesAsync(predecessorIds, ct);

        // The child's own stored status matters too: a task already under way is not "blocked",
        // whatever its predecessors say, and the gate exempts it for exactly that reason.
        var childStatuses = await requests.GetStoredStatusesAsync([.. children.Select(c => c.Id)], ct);

        var now = DateTime.UtcNow;

        var planChildren = children
            .OrderBy(c => c.SortOrder)
            .Select(child => new RequestPlanChild
            {
                Id = child.Id,
                Name = child.Name,
                PlanningMode = child.PlanningMode,
                ParentRequestId = child.ParentRequestId,
                Status = child.Status,
                StartTs = child.StartTs,
                EndTs = child.EndTs,
                SortOrder = child.SortOrder,
                Icon = child.Icon,
                PredecessorLogic = child.PredecessorLogic,
                PredecessorLogicK = child.PredecessorLogicK,
                CanStart = CanStart(child, predecessorsOf, predecessorsById, storedStatuses, childStatuses, now),
                ExternalPredecessorCount = externalPredecessors.GetValueOrDefault(child.Id),
                ExternalSuccessorCount = externalSuccessors.GetValueOrDefault(child.Id),
            })
            .ToList();

        return (planChildren, internalEdges);
    }

    private static bool CanStart(
        RequestInfo child,
        IReadOnlyDictionary<Guid, List<Guid>> predecessorsOf,
        IReadOnlyDictionary<Guid, RequestInfo> predecessorsById,
        IReadOnlyDictionary<Guid, RequestStatus> storedStatuses,
        IReadOnlyDictionary<Guid, RequestStatus> childStatuses,
        DateTime now)
    {
        // Work already under way, or finished, is not waiting on anything — showing it locked
        // would contradict the gate, which lets an in-progress request be saved unchallenged.
        var own = childStatuses.GetValueOrDefault(child.Id, RequestStatus.New);
        if (own is RequestStatus.InProgress or RequestStatus.Done) return true;
        if (child.Status is RequestStatus.InProgress or RequestStatus.Done) return true;

        if (!predecessorsOf.TryGetValue(child.Id, out var predecessorIds)) return true;

        var states = predecessorIds
            .Where(predecessorsById.ContainsKey)
            .Select(id => predecessorsById[id])
            .Select(p => new PredecessorState(
                p.Name,
                storedStatuses.GetValueOrDefault(p.Id, RequestStatus.New),
                p.StartTs,
                p.EndTs))
            .ToList();

        return JoinConditionEvaluator.EvaluateGate(JoinCondition.Of(child), states, now).IsMet;
    }
}

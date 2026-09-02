using System.ComponentModel;
using Api.Helpers;
using Api.Models;
using Api.Models.Insights;
using Api.Services;
using Api.Services.Insights;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Api.PlatformApi.Mcp;

/// <summary>
/// The questions an agent asks before it changes anything: what decides the finish date, what
/// depends on what, and where capacity runs out.
///
/// Every tool here is read-only, so the whole class is callable with a <c>schedule:read</c> token.
/// Each returns its service's own response record rather than a projection — those records are
/// already the wire shape the HTTP endpoints serialize, so re-describing them here would be a copy
/// that drifts the first time a field is added.
/// </summary>
[McpServerToolType]
public sealed class PlanningTools
{
    private readonly ICriticalPathService _criticalPath;
    private readonly IRequestDependencyService _dependencies;
    private readonly IRequestPlanService _plans;
    private readonly IInsightsService _insights;

    public PlanningTools(
        ICriticalPathService criticalPath,
        IRequestDependencyService dependencies,
        IRequestPlanService plans,
        IInsightsService insights)
    {
        _criticalPath = criticalPath;
        _dependencies = dependencies;
        _plans = plans;
        _insights = insights;
    }

    [McpServerTool(Name = "get_critical_path", Title = "Get the critical path",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Compute the chain of work that decides the finish date, and how much slack every "
        + "other request has. Each node reports isCritical and totalFloatDays: moving a critical "
        + "request moves the end date, moving one with float does not.")]
    public async Task<CriticalPathResult> GetCriticalPathAsync(
        [Description("Restrict to one site. Omit for the whole tenant. Get ids from list_sites.")]
        Guid? siteId = null,
        CancellationToken ct = default)
    {
        try
        {
            return await _criticalPath.ComputeAsync(siteId, ct);
        }
        catch (ConflictException ex)
        {
            // A cycle makes the critical path undefined, so there is no partial answer to return.
            // Name the tool that can still show the edges — it is the only way to find the loop.
            throw new McpException(
                $"The dependency graph contains a cycle, so no critical path exists: {ex.Message} "
                + "Call list_dependencies to see the edges and unlink_requests to break the loop.");
        }
    }

    [McpServerTool(Name = "list_dependencies", Title = "List dependencies",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List finish-to-start dependencies between requests. Give a requestId for just "
        + "that request's predecessors and successors, or omit it for every edge. This still works "
        + "when the graph has a cycle and get_critical_path cannot answer.")]
    public async Task<IReadOnlyList<RequestDependencyInfo>> ListDependenciesAsync(
        [Description("Only edges touching this request. Omit for every edge in scope.")]
        Guid? requestId = null,
        [Description("Restrict to one site. Ignored when requestId is given.")]
        Guid? siteId = null,
        CancellationToken ct = default)
    {
        if (requestId is null)
            return await _dependencies.GetAllAsync(siteId, ct);

        // Both directions in one list: an agent asking "what is this waiting on?" almost always
        // needs "and what waits on it?" in the same breath before it moves anything.
        var forRequest = await _dependencies.GetForRequestAsync(requestId.Value, ct);
        return [.. forRequest.Predecessors, .. forRequest.Successors];
    }

    [McpServerTool(Name = "get_request_plan", Title = "Get a request's plan",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get the sequenced plan of one request's children: every child with its status, "
        + "its start condition, whether it may start yet, and the dependencies among them. A child "
        + "carries a start condition over its predecessors — all of them, any one of them, or at "
        + "least k of them — and canStart says whether that condition is satisfied right now. Use "
        + "it to explain why a child is still blocked, and to choose what to schedule next.")]
    public async Task<RequestPlan> GetRequestPlanAsync(
        [Description("The parent request whose children form the plan.")]
        Guid requestId,
        CancellationToken ct = default)
    {
        return await _plans.GetPlanAsync(requestId, ct)
            ?? throw new McpException($"Request {requestId} was not found.");
    }

    [McpServerTool(Name = "analyze_capacity", Title = "Analyse capacity",
        ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Find where and when this tenant runs out of capacity. Returns the resources that "
        + "are overbooked, ranked worst first, together with the capacity-versus-demand trend that "
        + "explains them. Use it to answer which resource is the bottleneck.")]
    public async Task<CapacityAnalysis> AnalyzeCapacityAsync(
        [Description("Start of the window, ISO-8601 UTC.")] DateTime from,
        [Description("End of the window, ISO-8601 UTC.")] DateTime to,
        [Description("Restrict to one site. Omit for the whole tenant.")]
        Guid? siteId = null,
        [Description("Restrict to one resource type key, e.g. 'machine' or 'person'.")]
        string? resourceType = null,
        [Description("Trend bucket size: 'day', 'week' or 'month'. Defaults to the service's own.")]
        string? bucket = null,
        CancellationToken ct = default)
    {
        // One filter drives both queries — they answer halves of the same question, which is why
        // this is one tool rather than two. Two would invite fetching the ranking without the
        // trend that explains it.
        var filter = new InsightsFilter
        {
            SiteId = siteId,
            From = from,
            To = to,
            Bucket = bucket,
            ResourceType = resourceType,
        };

        var bottlenecks = await _insights.GetBottlenecksAsync(filter, ct);
        var trend = await _insights.GetUtilizationTrendAsync(filter, ct);

        return new CapacityAnalysis(bottlenecks, trend);
    }
}

/// <summary>
/// The two halves of the capacity question, composed rather than restated: which resources are
/// over capacity, and how demand tracked against capacity over the window.
/// </summary>
public sealed record CapacityAnalysis(
    InsightsBottlenecks Bottlenecks,
    InsightsUtilization Trend);

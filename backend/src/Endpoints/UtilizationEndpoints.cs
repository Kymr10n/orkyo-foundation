using Api.Helpers;
using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class UtilizationEndpoints
{
    public static void MapUtilizationEndpoints(this IEndpointRouteBuilder app)
    {
        var resources = app.MapGroup("/api/resources")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        resources.MapGet("/{id:guid}/utilization", async (
            Guid id,
            IUtilizationService service,
            DateTime from,
            DateTime to,
            string granularity = "day") =>
            {
                var result = await service.GetResourceUtilizationAsync(id, from, to, granularity);
                return EndpointHelpers.OkOrNotFound(result, "Resource", id);
            })
            .WithName("GetResourceUtilization")
            .WithSummary("Get utilization for a resource");

        var groups = app.MapGroup("/api/resource-groups")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        groups.MapGet("/{id:guid}/utilization", async (
            Guid id,
            IUtilizationService service,
            DateTime from,
            DateTime to,
            string granularity = "day") =>
            {
                var result = await service.GetGroupUtilizationAsync(id, from, to, granularity);
                return EndpointHelpers.OkOrNotFound(result, "Group", id);
            })
            .WithName("GetGroupUtilization")
            .WithSummary("Get utilization for a resource group");

        var tenant = app.MapGroup("/api/utilization")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        tenant.MapGet("/", async (
            IUtilizationService service,
            DateTime from,
            DateTime to,
            string? resourceTypeKey,
            string granularity = "day") =>
            Results.Ok(await service.GetTenantUtilizationAsync(resourceTypeKey, from, to, granularity)))
            .WithName("GetTenantUtilization")
            .WithSummary("Get aggregate utilization across all resources");

        tenant.MapGet("/by-resource", async (
            IUtilizationService service,
            DateTime from,
            DateTime to,
            string? resourceTypeKey,
            Guid? siteId,
            string granularity = "day") =>
            Results.Ok(await service.GetUtilizationByResourceAsync(resourceTypeKey, from, to, granularity, siteId)))
            .WithName("GetUtilizationByResource")
            .WithSummary("Get per-resource utilization in one response (bulk)");
    }
}

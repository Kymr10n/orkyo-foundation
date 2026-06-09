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
            .RequireTenantMembership();

        resources.MapGet("/{id:guid}/utilization", async (
            Guid id,
            IUtilizationService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime from,
            DateTime to,
            string granularity = "day") =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await service.GetResourceUtilizationAsync(id, from, to, granularity);
                return EndpointHelpers.OkOrNotFound(result, "Resource", id);
            }, logger, "get resource utilization", new { id, granularity }))
            .WithName("GetResourceUtilization")
            .WithSummary("Get utilization for a resource");

        var groups = app.MapGroup("/api/resource-groups")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireTenantMembership();

        groups.MapGet("/{id:guid}/utilization", async (
            Guid id,
            IUtilizationService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime from,
            DateTime to,
            string granularity = "day") =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await service.GetGroupUtilizationAsync(id, from, to, granularity);
                return EndpointHelpers.OkOrNotFound(result, "Group", id);
            }, logger, "get group utilization", new { id, granularity }))
            .WithName("GetGroupUtilization")
            .WithSummary("Get utilization for a resource group");

        var tenant = app.MapGroup("/api/utilization")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireTenantMembership();

        tenant.MapGet("/", async (
            IUtilizationService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime from,
            DateTime to,
            string? resourceTypeKey,
            string granularity = "day") =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await service.GetTenantUtilizationAsync(resourceTypeKey, from, to, granularity)),
            logger, "get tenant utilization", new { resourceTypeKey, granularity }))
            .WithName("GetTenantUtilization")
            .WithSummary("Get aggregate utilization across all resources");

        tenant.MapGet("/by-resource", async (
            IUtilizationService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime from,
            DateTime to,
            string? resourceTypeKey,
            string granularity = "day") =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await service.GetUtilizationByResourceAsync(resourceTypeKey, from, to, granularity)),
            logger, "get utilization by resource", new { resourceTypeKey, granularity }))
            .WithName("GetUtilizationByResource")
            .WithSummary("Get per-resource utilization in one response (bulk)");
    }
}

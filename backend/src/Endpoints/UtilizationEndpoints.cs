using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class UtilizationEndpoints
{
    // Every route here is member-readable and builds one bucket per resource per step, so each
    // window goes through TimeWindowQueryValidator (the Insights caps) before it reaches the service.
    public static void MapUtilizationEndpoints(this IEndpointRouteBuilder app)
    {
        var resources = app.MapGroup("/api/resources")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        resources.MapGet("/{id:guid}/utilization", async (
            Guid id,
            IUtilizationService service,
            IValidator<TimeWindowQuery> validator,
            DateTime? from,
            DateTime? to,
            CancellationToken ct,
            string granularity = "day") =>
            {
                var window = new TimeWindowQuery(from, to, granularity);
                return await EndpointHelpers.ExecuteAsync(window, validator, async () =>
                    EndpointHelpers.OkOrNotFound(
                        await service.GetResourceUtilizationAsync(id, window.FromUtc, window.ToUtc, granularity, ct),
                        "Resource", id));
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
            IValidator<TimeWindowQuery> validator,
            DateTime? from,
            DateTime? to,
            CancellationToken ct,
            string granularity = "day") =>
            {
                var window = new TimeWindowQuery(from, to, granularity);
                return await EndpointHelpers.ExecuteAsync(window, validator, async () =>
                    EndpointHelpers.OkOrNotFound(
                        await service.GetGroupUtilizationAsync(id, window.FromUtc, window.ToUtc, granularity, ct),
                        "Group", id));
            })
            .WithName("GetGroupUtilization")
            .WithSummary("Get utilization for a resource group");

        var tenant = app.MapGroup("/api/utilization")
            .WithTags("Utilization")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        tenant.MapGet("/", async (
            IUtilizationService service,
            IValidator<TimeWindowQuery> validator,
            DateTime? from,
            DateTime? to,
            string? resourceTypeKey,
            CancellationToken ct,
            string granularity = "day") =>
            {
                var window = new TimeWindowQuery(from, to, granularity);
                return await EndpointHelpers.ExecuteAsync(window, validator, async () =>
                    Results.Ok(await service.GetTenantUtilizationAsync(
                        resourceTypeKey, window.FromUtc, window.ToUtc, granularity, ct)));
            })
            .WithName("GetTenantUtilization")
            .WithSummary("Get aggregate utilization across all resources");

        tenant.MapGet("/by-resource", async (
            IUtilizationService service,
            IValidator<TimeWindowQuery> validator,
            DateTime? from,
            DateTime? to,
            string? resourceTypeKey,
            Guid? siteId,
            CancellationToken ct,
            string granularity = "day") =>
            {
                var window = new TimeWindowQuery(from, to, granularity);
                return await EndpointHelpers.ExecuteAsync(window, validator, async () =>
                    Results.Ok(await service.GetUtilizationByResourceAsync(
                        resourceTypeKey, window.FromUtc, window.ToUtc, granularity, siteId, ct)));
            })
            .WithName("GetUtilizationByResource")
            .WithSummary("Get per-resource utilization in one response (bulk)");
    }
}

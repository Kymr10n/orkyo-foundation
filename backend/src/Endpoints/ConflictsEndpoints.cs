using Api.Helpers;
using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// Tenant-wide conflicts registry — the authoritative, all-sites/all-dates view consumed by the
/// Conflicts page and the Requests-page badges. Separate from the utilization grid, which validates
/// only the scoped site + window it renders.
/// </summary>
public static class ConflictsEndpoints
{
    public static void MapConflictsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conflicts")
            .WithTags("Conflicts")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (
            [FromServices] IConflictService conflictService,
            ILogger<EndpointLoggerCategory> logger,
            CancellationToken ct) =>
            await EndpointHelpers.ExecuteAsync(
                async () => Results.Ok(await conflictService.GetAllAsync(ct)),
                logger, "list conflicts"))
            .WithName("ListConflicts")
            .WithSummary("Tenant-wide conflicts registry (all sites, all dates)");
    }
}

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
            DateTime? from,
            DateTime? to,
            [FromServices] IConflictService conflictService,
            CancellationToken ct) =>
            Results.Ok(await conflictService.GetAllAsync(from, to, ct)))
            .WithName("ListConflicts")
            .WithSummary("Tenant-wide conflicts registry — all-time, or scoped to [from,to] when supplied");
    }
}

using Api.Middleware;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

/// <summary>
/// The pre-configured resource type catalog: what can be activated, what the tenant made of
/// it, and the switches themselves.
/// </summary>
/// <remarks>
/// Admin-write for the same reason <see cref="ResourceTypeEndpoints"/> is: which kinds of
/// thing a tenant manages is governance. Reads stay member-open — the state feeds the UI.
/// No request bodies exist on this surface, so there is nothing for FluentValidation to
/// shape-check; the key is validated against the static catalog in the service.
/// </remarks>
public static class ResourceTypeCatalogEndpoints
{
    public static void MapResourceTypeCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-type-catalog")
            .WithTags("ResourceTypeCatalog")
            .RequireAuthorization()
            .RequireMemberReadAdminWrite();

        group.MapGet("/", async (IResourceTypeCatalogService service, CancellationToken ct) =>
            Results.Ok(await service.GetCatalogAsync(ct)))
            .WithName("GetResourceTypeCatalog")
            .WithSummary("Get the catalog with each entry's tenant state");

        group.MapPost("/{key}/activate", async (
            string key,
            IResourceTypeCatalogService service,
            CancellationToken ct) =>
            Results.Ok(await service.ActivateAsync(key, ct)))
            .WithName("ActivateCatalogResourceType")
            .WithSummary("Activate a catalog type, adopting the tenant's existing row when one exists");

        group.MapPost("/{key}/deactivate", async (
            string key,
            IResourceTypeCatalogService service,
            CancellationToken ct) =>
            await service.DeactivateAsync(key, ct)
                ? Results.NoContent()
                : Helpers.ErrorResponses.NotFoundMessage($"No resource type with key '{key}' exists"))
            .WithName("DeactivateCatalogResourceType")
            .WithSummary("Deactivate a catalog type, keeping its data");

        group.MapDelete("/{key}", async (
            string key,
            IResourceTypeCatalogService service,
            CancellationToken ct) =>
        {
            var result = await service.PurgeAsync(key, ct);
            return result is null
                ? Helpers.ErrorResponses.NotFoundMessage($"No resource type with key '{key}' exists")
                : Results.Ok(result);
        })
            .WithName("PurgeCatalogResourceType")
            .WithSummary("Delete a catalog type together with its resources and related data");
    }
}

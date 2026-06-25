using Api.Helpers;
using Api.Middleware;
using Api.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceTypeEndpoints
{
    public static void MapResourceTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-types")
            .WithTags("ResourceTypes")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (IResourceTypeRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetAllAsync()))
            .WithName("GetResourceTypes")
            .WithSummary("Get all resource types");

        group.MapGet("/{id:guid}", async (Guid id, IResourceTypeRepository repo, CancellationToken ct) =>
        {
            var rt = await repo.GetByIdAsync(id);
            return EndpointHelpers.OkOrNotFound(rt, "ResourceType", id);
        })
            .WithName("GetResourceTypeById")
            .WithSummary("Get a resource type by ID");
    }
}

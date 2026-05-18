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

        group.MapGet("/", async (IResourceTypeRepository repo, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetAllAsync()),
            logger, "list resource types"))
            .WithName("GetResourceTypes")
            .WithSummary("Get all resource types");

        group.MapGet("/{id:guid}", async (Guid id, IResourceTypeRepository repo, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var rt = await repo.GetByIdAsync(id);
                return rt is null ? ErrorResponses.NotFound("ResourceType", id) : Results.Ok(rt);
            }, logger, "get resource type", new { id }))
            .WithName("GetResourceTypeById")
            .WithSummary("Get a resource type by ID");
    }
}

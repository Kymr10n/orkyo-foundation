using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceGroupEndpoints
{
    public static void MapResourceGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-groups")
            .WithTags("Resource Groups")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (string resourceTypeKey, IResourceGroupRepository repo, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetByTypeKeyAsync(resourceTypeKey)),
            logger, "list resource groups", new { resourceTypeKey });
        })
        .WithName("GetResourceGroups")
        .WithSummary("List resource groups for a given resource type");

        group.MapGet("/{id:guid}", async (Guid id, IResourceGroupRepository repo, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await repo.GetByIdAsync(id);
                return result == null ? ErrorResponses.NotFound("Resource group", id) : Results.Ok(result);
            }, logger, "get resource group", new { id });
        })
        .WithName("GetResourceGroup")
        .WithSummary("Get a resource group by ID");

        group.MapPost("/", async (CreateResourceGroupRequest request, IResourceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, IValidator<CreateResourceGroupRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await repo.CreateAsync(request.ResourceTypeKey, request.Name, request.Description, request.DefaultAvailabilityPercent, request.Color, request.DisplayOrder);
                return Results.Created($"/api/resource-groups/{result.Id}", result);
            }, logger, "create resource group", new { name = request.Name });
        })
        .RequireAdminAccess()
        .WithName("CreateResourceGroup")
        .WithSummary("Create a new resource group");

        group.MapPut("/{id:guid}", async (Guid id, UpdateResourceGroupRequest request, IResourceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateResourceGroupRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await repo.UpdateAsync(id, request.Name, request.Description, request.DefaultAvailabilityPercent, request.Color, request.DisplayOrder);
                return result == null ? ErrorResponses.NotFound("Resource group", id) : Results.Ok(result);
            }, logger, "update resource group", new { id });
        })
        .RequireAdminAccess()
        .WithName("UpdateResourceGroup")
        .WithSummary("Update a resource group");

        group.MapDelete("/{id:guid}", async (Guid id, IResourceGroupRepository repo, CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repo.DeleteAsync(id);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Resource group", id);
            }, logger, "delete resource group", new { id });
        })
        .RequireAdminAccess()
        .WithName("DeleteResourceGroup")
        .WithSummary("Delete a resource group");
    }
}

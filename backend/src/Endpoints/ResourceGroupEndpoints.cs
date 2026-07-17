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
            .RequireMemberReadEditorWrite();

        group.MapGet("/", async (string resourceTypeKey, IResourceGroupRepository repo, CancellationToken ct) =>
        {
            return Results.Ok(await repo.GetByTypeKeyAsync(resourceTypeKey, ct));
        })
        .WithName("GetResourceGroups")
        .WithSummary("List resource groups for a given resource type");

        group.MapGet("/{id:guid}", async (Guid id, IResourceGroupRepository repo, CancellationToken ct) =>
        {
            var result = await repo.GetByIdAsync(id, ct);
            return EndpointHelpers.OkOrNotFound(result, "Resource group", id);
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
        .WithName("CreateResourceGroup")
        .WithSummary("Create a new resource group");

        group.MapPut("/{id:guid}", async (Guid id, UpdateResourceGroupRequest request, IResourceGroupRepository repo, ILogger<EndpointLoggerCategory> logger, IValidator<UpdateResourceGroupRequest> validator) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await repo.UpdateAsync(id, request.Name, request.Description, request.DefaultAvailabilityPercent, request.Color, request.DisplayOrder);
                return EndpointHelpers.OkOrNotFound(result, "Resource group", id);
            }, logger, "update resource group", new { id });
        })
        .WithName("UpdateResourceGroup")
        .WithSummary("Update a resource group");

        group.MapDelete("/{id:guid}", async (Guid id, IResourceGroupRepository repo, CancellationToken ct) =>
        {
            var deleted = await repo.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Resource group", id);
        })
        .WithName("DeleteResourceGroup")
        .WithSummary("Delete a resource group");
    }
}

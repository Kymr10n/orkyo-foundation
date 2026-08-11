using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceTypeEndpoints
{
    public static void MapResourceTypeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-types")
            .WithTags("ResourceTypes")
            .RequireAuthorization()
            // Defining the catalogue of things a tenant manages — and the custom fields each
            // one carries — is governance: it changes what every editor is asked to fill in.
            // Reads stay member-open because resource pages and forms list types.
            .RequireMemberReadAdminWrite();

        group.MapGet("/", async (IResourceTypeService service, bool? isActive, CancellationToken ct) =>
            Results.Ok(await service.GetAllAsync(isActive, ct)))
            .WithName("GetResourceTypes")
            .WithSummary("Get all resource types");

        group.MapGet("/{id:guid}", async (Guid id, IResourceTypeService service, CancellationToken ct) =>
        {
            var rt = await service.GetByIdAsync(id, ct);
            return EndpointHelpers.OkOrNotFound(rt, "ResourceType", id);
        })
            .WithName("GetResourceTypeById")
            .WithSummary("Get a resource type by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceTypeRequest request,
            IResourceTypeService service,
            IValidator<CreateResourceTypeRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/resource-types/{created.Id}", created);
            }, logger, "create resource type", new { request.Key }))
            .WithName("CreateResourceType")
            .WithSummary("Create a resource type");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateResourceTypeRequest request,
            IResourceTypeService service,
            IValidator<UpdateResourceTypeRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateAsync(id, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ResourceType", id);
            }, logger, "update resource type", new { id }))
            .WithName("UpdateResourceType")
            .WithSummary("Update a resource type");

        group.MapDelete("/{id:guid}", async (Guid id, IResourceTypeService service, CancellationToken ct) =>
        {
            var removed = await service.DeleteAsync(id, ct);
            return removed ? Results.NoContent() : ErrorResponses.NotFound("ResourceType", id);
        })
            .WithName("DeleteResourceType")
            .WithSummary("Delete a resource type, or deactivate it when resources still reference it");
    }
}

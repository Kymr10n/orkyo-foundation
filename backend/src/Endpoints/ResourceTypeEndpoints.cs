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
            .RequireMemberReadEditorWrite();

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

        // ── custom field definitions ──────────────────────────────────────────

        group.MapGet("/{id:guid}/fields", async (
            Guid id, bool? includeInactive, IResourceTypeService service, CancellationToken ct) =>
            Results.Ok(await service.GetFieldsAsync(id, includeInactive ?? false, ct)))
            .WithName("GetResourceTypeFields")
            .WithSummary("Get the custom field definitions of a resource type");

        group.MapPost("/{id:guid}/fields", async (
            Guid id,
            [FromBody] CreateResourceTypeFieldRequest request,
            IResourceTypeService service,
            IValidator<CreateResourceTypeFieldRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.AddFieldAsync(id, request, ct);
                return Results.Created($"/api/resource-types/{id}/fields/{created.Id}", created);
            }, logger, "create resource type field", new { id, request.Key }))
            .WithName("CreateResourceTypeField")
            .WithSummary("Add a custom field definition to a resource type");

        group.MapPut("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id,
            Guid fieldId,
            [FromBody] UpdateResourceTypeFieldRequest request,
            IResourceTypeService service,
            IValidator<UpdateResourceTypeFieldRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateFieldAsync(id, fieldId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ResourceTypeField", fieldId);
            }, logger, "update resource type field", new { id, fieldId }))
            .WithName("UpdateResourceTypeField")
            .WithSummary("Update a custom field definition");

        group.MapDelete("/{id:guid}/fields/{fieldId:guid}", async (
            Guid id, Guid fieldId, IResourceTypeService service, CancellationToken ct) =>
        {
            var deactivated = await service.DeactivateFieldAsync(id, fieldId, ct);
            return deactivated ? Results.NoContent() : ErrorResponses.NotFound("ResourceTypeField", fieldId);
        })
            .WithName("DeactivateResourceTypeField")
            .WithSummary("Deactivate a custom field definition (values are retained)");
    }
}

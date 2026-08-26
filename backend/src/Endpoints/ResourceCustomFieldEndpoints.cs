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

/// <summary>
/// Definitions of the descriptive fields a tenant puts on a resource type.
/// </summary>
/// <remarks>
/// Its own group rather than routes on <see cref="ResourceTypeEndpoints"/>, because the two
/// need different gates: defining a field changes what every editor in the tenant is asked to
/// fill in, which is governance, so writes here are Admin. Reads stay open to any member —
/// the resource form has to render the fields for the people entering values.
/// </remarks>
public static class ResourceCustomFieldEndpoints
{
    public static void MapResourceCustomFieldEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resource-types/{resourceTypeId:guid}/custom-fields")
            .WithTags("ResourceCustomFields")
            .RequireAuthorization()
            .RequireMemberReadAdminWrite();

        group.MapGet("/", async (
            Guid resourceTypeId,
            IResourceCustomFieldService service,
            CancellationToken ct) =>
        {
            var fields = await service.GetByResourceTypeAsync(resourceTypeId, ct);
            return EndpointHelpers.OkOrNotFound(fields, "ResourceType", resourceTypeId);
        })
            .WithName("GetResourceCustomFields")
            .WithSummary("Get the custom fields defined on a resource type");

        group.MapGet("/{fieldId:guid}", async (
            Guid resourceTypeId,
            Guid fieldId,
            IResourceCustomFieldService service,
            CancellationToken ct) =>
        {
            var field = await service.GetByIdAsync(resourceTypeId, fieldId, ct);
            return EndpointHelpers.OkOrNotFound(field, "ResourceCustomField", fieldId);
        })
            .WithName("GetResourceCustomField")
            .WithSummary("Get one custom field by ID");

        group.MapPost("/", async (
            Guid resourceTypeId,
            [FromBody] CreateResourceCustomFieldRequest request,
            IResourceCustomFieldService service,
            IValidator<CreateResourceCustomFieldRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateAsync(resourceTypeId, request, ct);
                if (created is null) return ErrorResponses.NotFound("ResourceType", resourceTypeId);

                return Results.Created(
                    $"/api/resource-types/{resourceTypeId}/custom-fields/{created.Id}", created);
            }, logger, "create resource custom field", new { resourceTypeId, request.Key }))
            .WithName("CreateResourceCustomField")
            .WithSummary("Define a custom field on a resource type");

        group.MapPut("/{fieldId:guid}", async (
            Guid resourceTypeId,
            Guid fieldId,
            [FromBody] UpdateResourceCustomFieldRequest request,
            IResourceCustomFieldService service,
            IValidator<UpdateResourceCustomFieldRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateAsync(resourceTypeId, fieldId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ResourceCustomField", fieldId);
            }, logger, "update resource custom field", new { resourceTypeId, fieldId }))
            .WithName("UpdateResourceCustomField")
            .WithSummary("Update a custom field's label, description, requiredness, order or activation");

        group.MapDelete("/{fieldId:guid}", async (
            Guid resourceTypeId,
            Guid fieldId,
            IResourceCustomFieldService service,
            CancellationToken ct) =>
        {
            var removed = await service.DeleteAsync(resourceTypeId, fieldId, ct);
            return EndpointHelpers.NoContentOrNotFound(removed, "ResourceCustomField", fieldId);
        })
            .WithName("DeleteResourceCustomField")
            .WithSummary("Delete a custom field and discard the values resources hold for it");
    }
}

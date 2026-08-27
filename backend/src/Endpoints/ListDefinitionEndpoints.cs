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
/// List definitions, their columns, and the shared instances built from them.
/// </summary>
/// <remarks>
/// Admin-write for the same reason <see cref="ResourceCustomFieldEndpoints"/> is: reshaping a
/// list changes what every editor in the tenant is asked to fill in, which is governance. Reads
/// stay open to any member, because the row form has to render the columns for the people
/// entering data. The rows themselves are the other side of that line and live in
/// <see cref="ListInstanceEndpoints"/>.
/// </remarks>
public static class ListDefinitionEndpoints
{
    public static void MapListDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/list-definitions")
            .WithTags("ListDefinitions")
            .RequireAuthorization()
            .RequireMemberReadAdminWrite();

        group.MapGet("/", async (
            [FromQuery] bool? includeInactive,
            [FromQuery] string? scope,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            if (scope is not null && !ListDefinitionScopes.All.Contains(scope))
                return ErrorResponses.BadRequest(
                    $"Unknown scope '{scope}'. Valid scopes: {string.Join(", ", ListDefinitionScopes.All)}");
            return Results.Ok(await service.GetAllAsync(includeInactive ?? false, scope, ct));
        })
            .WithName("GetListDefinitions")
            .WithSummary("Get the list definitions this tenant has defined");

        group.MapGet("/{definitionId:guid}", async (
            Guid definitionId,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            var definition = await service.GetByIdAsync(definitionId, ct);
            return EndpointHelpers.OkOrNotFound(definition, "ListDefinition", definitionId);
        })
            .WithName("GetListDefinition")
            .WithSummary("Get one list definition with its columns");

        group.MapPost("/", async (
            [FromBody] CreateListDefinitionRequest request,
            IListDefinitionService service,
            IValidator<CreateListDefinitionRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateAsync(request, ct);
                return Results.Created($"/api/list-definitions/{created.Id}", created);
            }, logger, "create list definition", new { request.Name }))
            .WithName("CreateListDefinition")
            .WithSummary("Define a reusable list shape");

        group.MapPut("/{definitionId:guid}", async (
            Guid definitionId,
            [FromBody] UpdateListDefinitionRequest request,
            IListDefinitionService service,
            IValidator<UpdateListDefinitionRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateAsync(definitionId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ListDefinition", definitionId);
            }, logger, "update list definition", new { definitionId }))
            .WithName("UpdateListDefinition")
            .WithSummary("Rename a list definition, or retire it");

        group.MapDelete("/{definitionId:guid}", async (
            Guid definitionId,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            var removed = await service.DeleteAsync(definitionId, ct);
            return EndpointHelpers.NoContentOrNotFound(removed, "ListDefinition", definitionId);
        })
            .WithName("DeleteListDefinition")
            .WithSummary("Delete a list definition that nothing references");

        // ── Columns ───────────────────────────────────────────────────────────
        group.MapPost("/{definitionId:guid}/columns", async (
            Guid definitionId,
            [FromBody] CreateListColumnRequest request,
            IListDefinitionService service,
            IValidator<CreateListColumnRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateColumnAsync(definitionId, request, ct);
                if (created is null) return ErrorResponses.NotFound("ListDefinition", definitionId);

                return Results.Created($"/api/list-definitions/{definitionId}/columns/{created.Id}", created);
            }, logger, "create list column", new { definitionId, request.Key }))
            .WithName("CreateListColumn")
            .WithSummary("Add a column to a list definition");

        group.MapPut("/{definitionId:guid}/columns/{columnId:guid}", async (
            Guid definitionId,
            Guid columnId,
            [FromBody] UpdateListColumnRequest request,
            IListDefinitionService service,
            IValidator<UpdateListColumnRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateColumnAsync(definitionId, columnId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ListColumn", columnId);
            }, logger, "update list column", new { definitionId, columnId }))
            .WithName("UpdateListColumn")
            .WithSummary("Update a column's label, options, requiredness, order or activation");

        group.MapDelete("/{definitionId:guid}/columns/{columnId:guid}", async (
            Guid definitionId,
            Guid columnId,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            var removed = await service.DeleteColumnAsync(definitionId, columnId, ct);
            return EndpointHelpers.NoContentOrNotFound(removed, "ListColumn", columnId);
        })
            .WithName("DeleteListColumn")
            .WithSummary("Delete a column and discard the cells rows hold for it");

        // ── Shared instances ──────────────────────────────────────────────────
        group.MapGet("/{definitionId:guid}/instances", async (
            Guid definitionId,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            var instances = await service.GetSharedInstancesAsync(definitionId, ct);
            return EndpointHelpers.OkOrNotFound(instances, "ListDefinition", definitionId);
        })
            .WithName("GetSharedListInstances")
            .WithSummary("Get the shared instances of a list definition");

        group.MapPost("/{definitionId:guid}/instances", async (
            Guid definitionId,
            [FromBody] CreateListInstanceRequest request,
            IListDefinitionService service,
            IValidator<CreateListInstanceRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateSharedInstanceAsync(definitionId, request, ct);
                if (created is null) return ErrorResponses.NotFound("ListDefinition", definitionId);

                return Results.Created($"/api/list-instances/{created.Id}", created);
            }, logger, "create shared list instance", new { definitionId, request.Name }))
            .WithName("CreateSharedListInstance")
            .WithSummary("Create a named shared instance of a list definition");

        group.MapPut("/{definitionId:guid}/instances/{instanceId:guid}", async (
            Guid definitionId,
            Guid instanceId,
            [FromBody] UpdateListInstanceRequest request,
            IListDefinitionService service,
            IValidator<UpdateListInstanceRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateSharedInstanceAsync(definitionId, instanceId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ListInstance", instanceId);
            }, logger, "update shared list instance", new { definitionId, instanceId }))
            .WithName("UpdateSharedListInstance")
            .WithSummary("Rename a shared list instance");

        group.MapDelete("/{definitionId:guid}/instances/{instanceId:guid}", async (
            Guid definitionId,
            Guid instanceId,
            IListDefinitionService service,
            CancellationToken ct) =>
        {
            var removed = await service.DeleteSharedInstanceAsync(definitionId, instanceId, ct);
            return EndpointHelpers.NoContentOrNotFound(removed, "ListInstance", instanceId);
        })
            .WithName("DeleteSharedListInstance")
            .WithSummary("Delete a shared list instance that no field references");
    }
}

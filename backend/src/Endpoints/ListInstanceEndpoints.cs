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
/// Rows of a list, and the resolver that finds the instance behind one resource's list field.
/// </summary>
/// <remarks>
/// Editor-write, unlike <see cref="ListDefinitionEndpoints"/>: filling a list in is ordinary
/// data entry, the same as any other value on a resource. The split is deliberate — an editor
/// may add a maintenance entry but not decide what a maintenance entry consists of.
///
/// All row CRUD is keyed by instance id and lives on one group, for both kinds of instance:
/// the rows of a shared instance and of a per-resource one are the same rows, and giving them
/// two routes would double the surface to say nothing new.
/// </remarks>
public static class ListInstanceEndpoints
{
    public static void MapListInstanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/list-instances/{instanceId:guid}")
            .WithTags("ListInstances")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        group.MapGet("/", async (
            Guid instanceId,
            IListRowService service,
            CancellationToken ct) =>
        {
            var instance = await service.GetInstanceAsync(instanceId, ct);
            return EndpointHelpers.OkOrNotFound(instance, "ListInstance", instanceId);
        })
            .WithName("GetListInstance")
            .WithSummary("Get a list instance");

        group.MapGet("/rows", async (
            Guid instanceId,
            IListRowService service,
            CancellationToken ct) =>
        {
            var rows = await service.GetRowsAsync(instanceId, ct);
            return EndpointHelpers.OkOrNotFound(rows, "ListInstance", instanceId);
        })
            .WithName("GetListRows")
            .WithSummary("Get the rows of a list instance");

        group.MapPost("/rows", async (
            Guid instanceId,
            [FromBody] ListRowRequest request,
            IListRowService service,
            IValidator<ListRowRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var created = await service.CreateRowAsync(instanceId, request, ct);
                if (created is null) return ErrorResponses.NotFound("ListInstance", instanceId);

                return Results.Created($"/api/list-instances/{instanceId}/rows/{created.Id}", created);
            }, logger, "create list row", new { instanceId }))
            .WithName("CreateListRow")
            .WithSummary("Add a row to a list instance");

        group.MapPut("/rows/{rowId:guid}", async (
            Guid instanceId,
            Guid rowId,
            [FromBody] ListRowRequest request,
            IListRowService service,
            IValidator<ListRowRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var updated = await service.UpdateRowAsync(instanceId, rowId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "ListRow", rowId);
            }, logger, "update list row", new { instanceId, rowId }))
            .WithName("UpdateListRow")
            .WithSummary("Replace the cells of one row");

        group.MapDelete("/rows/{rowId:guid}", async (
            Guid instanceId,
            Guid rowId,
            IListRowService service,
            CancellationToken ct) =>
        {
            var removed = await service.DeleteRowAsync(instanceId, rowId, ct);
            return removed ? Results.NoContent() : ErrorResponses.NotFound("ListRow", rowId);
        })
            .WithName("DeleteListRow")
            .WithSummary("Delete a row, and drop it from any resource that selected it");

        // ── Per-resource resolver ─────────────────────────────────────────────
        // Separate group: it hangs off a resource, not off an instance id, because the client
        // holds a resource and a field and does not yet know whether an instance exists.
        var resolver = app.MapGroup("/api/resources/{resourceId:guid}/list-fields/{fieldId:guid}")
            .WithTags("ListInstances")
            .RequireAuthorization()
            .RequireMemberReadEditorWrite();

        resolver.MapGet("/instance", async (
            Guid resourceId,
            Guid fieldId,
            IListRowService service,
            CancellationToken ct) =>
        {
            var instance = await service.ResolveResourceInstanceAsync(resourceId, fieldId, ct);
            // 404 until the first write: a read never creates a holder, so a resource whose list
            // is still empty simply has nothing here yet.
            return EndpointHelpers.OkOrNotFound(instance, "ListInstance", fieldId);
        })
            .WithName("GetResourceListInstance")
            .WithSummary("Get the list instance for a resource's list field, if it exists yet");

        resolver.MapPost("/instance", async (
            Guid resourceId,
            Guid fieldId,
            IListRowService service,
            CancellationToken ct) =>
        {
            var instance = await service.EnsureResourceInstanceAsync(resourceId, fieldId, ct);
            if (instance is null) return ErrorResponses.NotFound("ListInstance", fieldId);

            // 200 rather than 201: the call is idempotent and the caller cannot tell — nor need
            // it — whether this request was the one that created the instance.
            return Results.Ok(instance);
        })
            .WithName("EnsureResourceListInstance")
            .WithSummary("Get or create the list instance for a resource's list field");
    }
}

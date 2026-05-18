using System.Text.Json;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class ResourceEndpoints
{
    public static void MapResourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/resources")
            .WithTags("Resources")
            .RequireAuthorization()
            .RequireTenantMembership();

        group.MapGet("/", async (
            IResourceService service,
            ILogger<EndpointLoggerCategory> logger,
            string? resourceTypeKey,
            bool? isActive,
            string? search) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var items = await service.GetAllAsync(new ResourceListFilter
                {
                    ResourceTypeKey = resourceTypeKey,
                    IsActive = isActive,
                    Search = search,
                });
                return Results.Ok(new { data = items, total = items.Count, page = 1, pageSize = items.Count });
            },
            logger, "list resources"))
            .WithName("GetResources")
            .WithSummary("Get all resources");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IResourceService service,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.GetByIdAsync(id);
                return r is null ? ErrorResponses.NotFound("Resource", id) : Results.Ok(r);
            }, logger, "get resource", new { id }))
            .WithName("GetResourceById")
            .WithSummary("Get a resource by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceRequest request,
            IResourceService service,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.CreateAsync(request);
                return Results.Created($"/api/resources/{r.Id}", r);
            }, logger, "create resource"))
            .RequireAdminAccess()
            .WithName("CreateResource")
            .WithSummary("Create a new resource");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateResourceRequest request,
            IResourceService service,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var r = await service.UpdateAsync(id, request);
                return r is null ? ErrorResponses.NotFound("Resource", id) : Results.Ok(r);
            }, logger, "update resource", new { id }))
            .RequireAdminAccess()
            .WithName("UpdateResource")
            .WithSummary("Update a resource");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IResourceService service,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deactivated = await service.DeactivateAsync(id);
                return deactivated ? Results.NoContent() : ErrorResponses.NotFound("Resource", id);
            }, logger, "deactivate resource", new { id }))
            .RequireAdminAccess()
            .WithName("DeactivateResource")
            .WithSummary("Deactivate (soft-delete) a resource");

        group.MapGet("/{id:guid}/assignments", async (
            Guid id,
            IResourceAssignmentService service,
            ILogger<EndpointLoggerCategory> logger,
            DateTime? from,
            DateTime? to) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var fromUtc = from ?? DateTime.UtcNow.AddDays(-30);
                var toUtc = to ?? DateTime.UtcNow.AddDays(90);
                return Results.Ok(await service.GetByResourceAsync(id, fromUtc, toUtc));
            }, logger, "get resource assignments", new { id }))
            .WithName("GetResourceAssignments")
            .WithSummary("Get assignments for a resource");

        // ── Capabilities ──────────────────────────────────────────────

        group.MapGet("/{id:guid}/capabilities", async (
            Guid id,
            IResourceService service,
            IResourceCapabilityRepository repository,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (await service.GetByIdAsync(id) is null)
                    return ErrorResponses.NotFound("Resource", id);
                return Results.Ok(await repository.GetByResourceAsync(id));
            }, logger, "get resource capabilities", new { id }))
            .WithName("GetResourceCapabilities")
            .WithSummary("Get all capabilities for a resource");

        group.MapPost("/{id:guid}/capabilities", async (
            Guid id,
            AddResourceCapabilityRequest request,
            IResourceService service,
            IResourceCapabilityRepository repository,
            ICriteriaService criteriaService,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var resource = await service.GetByIdAsync(id);
                if (resource is null)
                    return ErrorResponses.NotFound("Resource", id);

                var criterion = await criteriaService.GetByIdAsync(request.CriterionId, ct);
                if (criterion is null)
                    return ErrorResponses.NotFound("Criterion", request.CriterionId);

                if (!criterion.ResourceTypeKeys.Contains(resource.ResourceTypeKey, StringComparer.Ordinal))
                    return ErrorResponses.BadRequest(
                        $"Criterion '{criterion.Name}' is not applicable to resource type '{resource.ResourceTypeKey}'.");

                var capability = await repository.UpsertAsync(id, request.CriterionId, request.Value);
                return Results.Created($"/api/resources/{id}/capabilities/{capability.Id}", capability);
            }, logger, "add resource capability", new { id, criterionId = request.CriterionId }))
            .WithName("AddResourceCapability")
            .WithSummary("Add or update a capability for a resource");

        group.MapDelete("/{id:guid}/capabilities/{capabilityId:guid}", async (
            Guid id,
            Guid capabilityId,
            IResourceCapabilityRepository repository,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var deleted = await repository.DeleteAsync(id, capabilityId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Capability", capabilityId);
            }, logger, "delete resource capability", new { id, capabilityId }))
            .WithName("DeleteResourceCapability")
            .WithSummary("Remove a capability from a resource");

        // ── Absences ──────────────────────────────────────────────────

        group.MapGet("/{id:guid}/absences", async (
            Guid id,
            Guid siteId,
            ISchedulingRepository scheduling,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await scheduling.GetOffTimesByResourceAsync(id, siteId)),
            logger, "get resource absences", new { id, siteId }))
            .WithName("GetResourceAbsences")
            .WithSummary("Get all absences (off-times) for a resource within a site");

        group.MapPost("/{id:guid}/absences", async (
            Guid id,
            [FromBody] CreateResourceAbsenceRequest request,
            ISchedulingRepository scheduling,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var offtime = await scheduling.CreateOffTimeAsync(request.SiteId, new CreateOffTimeRequest
                {
                    Title = request.Title,
                    Type = request.Type,
                    AppliesToAllResources = false,
                    ResourceIds = [id],
                    StartTs = request.StartTs,
                    EndTs = request.EndTs,
                    IsRecurring = request.IsRecurring,
                    RecurrenceRule = request.RecurrenceRule,
                    Enabled = request.Enabled,
                });
                return Results.Created($"/api/resources/{id}/absences/{offtime.Id}", offtime);
            }, logger, "create resource absence", new { id }))
            .RequireAdminAccess()
            .WithName("CreateResourceAbsence")
            .WithSummary("Create an absence (off-time) for a resource");

        group.MapPut("/{id:guid}/absences/{absenceId:guid}", async (
            Guid id,
            Guid absenceId,
            [FromBody] UpdateResourceAbsenceRequest request,
            ISchedulingRepository scheduling,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var existing = await scheduling.GetOffTimeByIdAsync(absenceId);
                if (existing is null || existing.ResourceIds?.Contains(id) != true)
                    return ErrorResponses.NotFound("Absence", absenceId);

                var updated = await scheduling.UpdateOffTimeAsync(existing.SiteId, absenceId,
                    new UpdateOffTimeRequest
                    {
                        Title = request.Title,
                        Type = request.Type,
                        StartTs = request.StartTs,
                        EndTs = request.EndTs,
                        IsRecurring = request.IsRecurring,
                        RecurrenceRule = request.RecurrenceRule,
                        Enabled = request.Enabled,
                    });
                return updated is null ? ErrorResponses.NotFound("Absence", absenceId) : Results.Ok(updated);
            }, logger, "update resource absence", new { id, absenceId }))
            .RequireAdminAccess()
            .WithName("UpdateResourceAbsence")
            .WithSummary("Update an absence (off-time) for a resource");

        group.MapDelete("/{id:guid}/absences/{absenceId:guid}", async (
            Guid id,
            Guid absenceId,
            ISchedulingRepository scheduling,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var existing = await scheduling.GetOffTimeByIdAsync(absenceId);
                if (existing is null || existing.ResourceIds?.Contains(id) != true)
                    return ErrorResponses.NotFound("Absence", absenceId);

                var deleted = await scheduling.DeleteOffTimeAsync(existing.SiteId, absenceId);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Absence", absenceId);
            }, logger, "delete resource absence", new { id, absenceId }))
            .RequireAdminAccess()
            .WithName("DeleteResourceAbsence")
            .WithSummary("Delete an absence (off-time) for a resource");
    }
}

public record AddResourceCapabilityRequest(Guid CriterionId, JsonElement Value);

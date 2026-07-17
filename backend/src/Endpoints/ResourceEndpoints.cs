using System.Text.Json;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using Api.Services;
using FluentValidation;
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
            .RequireMemberReadEditorWrite();

        group.MapGet("/", async (
            IResourceService service,
            string? resourceTypeKey,
            bool? isActive,
            string? search) =>
        {
            var items = await service.GetAllAsync(new ResourceListFilter
            {
                ResourceTypeKey = resourceTypeKey,
                IsActive = isActive,
                Search = search,
            });
            return Results.Ok(new { data = items, total = items.Count, page = 1, pageSize = items.Count });
        })
            .WithName("GetResources")
            .WithSummary("Get all resources");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IResourceService service,
            CancellationToken ct) =>
        {
            var r = await service.GetByIdAsync(id, ct);
            return EndpointHelpers.OkOrNotFound(r, "Resource", id);
        })
            .WithName("GetResourceById")
            .WithSummary("Get a resource by ID");

        group.MapPost("/", async (
            [FromBody] CreateResourceRequest request,
            IResourceService service,
            CancellationToken ct) =>
        {
            var r = await service.CreateAsync(request, ct);
            return Results.Created($"/api/resources/{r.Id}", r);
        })
            .WithName("CreateResource")
            .WithSummary("Create a new resource");

        group.MapPut("/{id:guid}", async (
            Guid id,
            [FromBody] UpdateResourceRequest request,
            IResourceService service,
            CancellationToken ct) =>
        {
            var r = await service.UpdateAsync(id, request, ct);
            return EndpointHelpers.OkOrNotFound(r, "Resource", id);
        })
            .WithName("UpdateResource")
            .WithSummary("Update a resource");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            IResourceService service,
            CancellationToken ct) =>
        {
            var deactivated = await service.DeactivateAsync(id, ct);
            return deactivated ? Results.NoContent() : ErrorResponses.NotFound("Resource", id);
        })
            .WithName("DeactivateResource")
            .WithSummary("Deactivate (soft-delete) a resource");

        group.MapGet("/{id:guid}/assignments", async (
            Guid id,
            IResourceAssignmentService service,
            DateTime? from,
            DateTime? to) =>
        {
            var fromUtc = from ?? DateTime.UtcNow.AddDays(-30);
            var toUtc = to ?? DateTime.UtcNow.AddDays(90);
            return Results.Ok(await service.GetByResourceAsync(id, fromUtc, toUtc));
        })
            .WithName("GetResourceAssignments")
            .WithSummary("Get assignments for a resource");

        group.MapGet("/{id:guid}/candidate-requests", async (
            Guid id,
            IRequestRepository requestRepository,
            IResourceService resourceService,
            ICapabilityMatcher capabilityMatcher,
            DateTime start,
            DateTime end,
            CancellationToken ct) =>
        {
            if (await resourceService.GetByIdAsync(id, ct) is null)
                return ErrorResponses.NotFound("Resource", id);

            var candidates = await requestRepository.GetCandidatesOverlappingAsync(id, start, end, ct);

            var result = new List<CandidateRequestInfo>(candidates.Count);
            foreach (var (req, assignmentId) in candidates)
            {
                var requirements = new List<CandidateRequirementInfo>(req.Requirements?.Count ?? 0);
                foreach (var r in req.Requirements ?? [])
                {
                    var satisfied = await capabilityMatcher.ResourceSatisfiesRequirementAsync(id, r, ct);
                    requirements.Add(new CandidateRequirementInfo(r.Criterion?.Name ?? r.CriterionId.ToString(), satisfied));
                }
                result.Add(new CandidateRequestInfo(req.Id, req.Name, req.StartTs, req.EndTs, requirements, assignmentId));
            }
            return Results.Ok(result);
        })
            .WithName("GetResourceCandidateRequests")
            .WithSummary("Get active requests overlapping a period that are not yet assigned to this resource");

        // ── Capabilities ──────────────────────────────────────────────

        group.MapGet("/{id:guid}/capabilities", async (
            Guid id,
            IResourceService service,
            IResourceCapabilityRepository repository,
            CancellationToken ct) =>
        {
            if (await service.GetByIdAsync(id, ct) is null)
                return ErrorResponses.NotFound("Resource", id);
            return Results.Ok(await repository.GetByResourceAsync(id, ct));
        })
            .WithName("GetResourceCapabilities")
            .WithSummary("Get all capabilities for a resource");

        group.MapPost("/{id:guid}/capabilities", async (
            Guid id,
            AddResourceCapabilityRequest request,
            IResourceService service,
            IResourceCapabilityRepository repository,
            ICriteriaService criteriaService,
            CancellationToken ct) =>
        {
            var resource = await service.GetByIdAsync(id, ct);
            if (resource is null)
                return ErrorResponses.NotFound("Resource", id);

            var criterion = await criteriaService.GetByIdAsync(request.CriterionId, ct);
            if (criterion is null)
                return ErrorResponses.NotFound("Criterion", request.CriterionId);

            if (!criterion.ResourceTypeKeys.Contains(resource.ResourceTypeKey, StringComparer.Ordinal))
                return ErrorResponses.BadRequest(
                    $"Criterion '{criterion.Name}' is not applicable to resource type '{resource.ResourceTypeKey}'.");

            var capability = await repository.UpsertAsync(id, request.CriterionId, request.Value, ct);
            return Results.Created($"/api/resources/{id}/capabilities/{capability.Id}", capability);
        })
            .WithName("AddResourceCapability")
            .WithSummary("Add or update a capability for a resource");

        group.MapDelete("/{id:guid}/capabilities/{capabilityId:guid}", async (
            Guid id,
            Guid capabilityId,
            IResourceCapabilityRepository repository,
            CancellationToken ct) =>
        {
            var deleted = await repository.DeleteAsync(id, capabilityId, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Capability", capabilityId);
        })
            .WithName("DeleteResourceCapability")
            .WithSummary("Remove a capability from a resource");

        // ── Absences ──────────────────────────────────────────────────

        group.MapGet("/{id:guid}/absences", async (
            Guid id,
            IResourceAbsenceRepository absenceRepo,
            CancellationToken ct) =>
            Results.Ok(await absenceRepo.GetByResourceAsync(id, ct)))
            .WithName("GetResourceAbsences")
            .WithSummary("Get all absences for a resource");

        group.MapPost("/{id:guid}/absences", async (
            Guid id,
            [FromBody] CreateResourceAbsenceRequest request,
            IResourceService resourceService,
            IResourceAbsenceRepository absenceRepo,
            IValidator<CreateResourceAbsenceRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var resource = await resourceService.GetByIdAsync(id);
                if (resource is null) return ErrorResponses.NotFound("Resource", id);
                var absence = await absenceRepo.CreateAsync(id, request, ct);
                return Results.Created($"/api/resources/{id}/absences/{absence.Id}", absence);
            }, logger, "create resource absence", new { id }))
            .WithName("CreateResourceAbsence")
            .WithSummary("Create an absence for a resource");

        group.MapPut("/{id:guid}/absences/{absenceId:guid}", async (
            Guid id,
            Guid absenceId,
            [FromBody] UpdateResourceAbsenceRequest request,
            IResourceAbsenceRepository absenceRepo,
            IValidator<UpdateResourceAbsenceRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var existing = await absenceRepo.GetByIdAsync(absenceId, ct);
                if (existing is null || existing.ResourceId != id)
                    return ErrorResponses.NotFound("Absence", absenceId);
                var updated = await absenceRepo.UpdateAsync(absenceId, request, ct);
                return EndpointHelpers.OkOrNotFound(updated, "Absence", absenceId);
            }, logger, "update resource absence", new { id, absenceId }))
            .WithName("UpdateResourceAbsence")
            .WithSummary("Update an absence for a resource");

        group.MapDelete("/{id:guid}/absences/{absenceId:guid}", async (
            Guid id,
            Guid absenceId,
            IResourceAbsenceRepository absenceRepo,
            CancellationToken ct) =>
        {
            var existing = await absenceRepo.GetByIdAsync(absenceId, ct);
            if (existing is null || existing.ResourceId != id)
                return ErrorResponses.NotFound("Absence", absenceId);
            var deleted = await absenceRepo.DeleteAsync(absenceId, ct);
            return deleted ? Results.NoContent() : ErrorResponses.NotFound("Absence", absenceId);
        })
            .WithName("DeleteResourceAbsence")
            .WithSummary("Delete an absence for a resource");
    }
}

public record AddResourceCapabilityRequest(Guid CriterionId, JsonElement Value);

using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Repositories;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class AvailabilityEventEndpoints
{
    public static void MapAvailabilityEventEndpoints(this IEndpointRouteBuilder app)
    {
        var events = app.MapGroup("/api/sites/{siteId:guid}/availability-events")
            .WithTags("AvailabilityEvents")
            .RequireAuthorization()
            .RequireTenantMembership();

        events.MapGet("/", async (
            Guid siteId,
            IAvailabilityEventRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
                Results.Ok(await repo.GetBySiteAsync(siteId, ct)),
            logger, "list availability events", new { siteId }))
            .WithName("GetAvailabilityEvents")
            .WithSummary("List all availability events for a site");

        events.MapGet("/{eventId:guid}", async (
            Guid siteId,
            Guid eventId,
            IAvailabilityEventRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var result = await repo.GetByIdAsync(eventId, ct);
                return result is null || result.SiteId != siteId
                    ? ErrorResponses.NotFound("AvailabilityEvent", eventId)
                    : Results.Ok(result);
            }, logger, "get availability event", new { siteId, eventId }))
            .WithName("GetAvailabilityEventById")
            .WithSummary("Get a specific availability event");

        events.MapPost("/", async (
            Guid siteId,
            [FromBody] CreateAvailabilityEventRequest request,
            IAvailabilityEventRepository repo,
            IValidator<CreateAvailabilityEventRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var result = await repo.CreateAsync(siteId, request, ct);
                return Results.Created($"/api/sites/{siteId}/availability-events/{result.Id}", result);
            }, logger, "create availability event", new { siteId }))
            .RequireAdminAccess()
            .WithName("CreateAvailabilityEvent")
            .WithSummary("Create a new availability event for a site");

        events.MapPut("/{eventId:guid}", async (
            Guid siteId,
            Guid eventId,
            [FromBody] UpdateAvailabilityEventRequest request,
            IAvailabilityEventRepository repo,
            IValidator<UpdateAvailabilityEventRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var existing = await repo.GetByIdAsync(eventId, ct);
                if (existing is null || existing.SiteId != siteId)
                    return ErrorResponses.NotFound("AvailabilityEvent", eventId);
                var result = await repo.UpdateAsync(eventId, request, ct);
                return result is null ? ErrorResponses.NotFound("AvailabilityEvent", eventId) : Results.Ok(result);
            }, logger, "update availability event", new { siteId, eventId }))
            .RequireAdminAccess()
            .WithName("UpdateAvailabilityEvent")
            .WithSummary("Update an availability event");

        events.MapDelete("/{eventId:guid}", async (
            Guid siteId,
            Guid eventId,
            IAvailabilityEventRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var existing = await repo.GetByIdAsync(eventId, ct);
                if (existing is null || existing.SiteId != siteId)
                    return ErrorResponses.NotFound("AvailabilityEvent", eventId);
                var deleted = await repo.DeleteAsync(eventId, ct);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("AvailabilityEvent", eventId);
            }, logger, "delete availability event", new { siteId, eventId }))
            .RequireAdminAccess()
            .WithName("DeleteAvailabilityEvent")
            .WithSummary("Delete an availability event");

        // ── Scopes ─────────────────────────────────────────────────────────

        events.MapPost("/{eventId:guid}/scopes", async (
            Guid siteId,
            Guid eventId,
            [FromBody] AddScopeRequest request,
            IAvailabilityEventRepository repo,
            IValidator<AddScopeRequest> validator,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var existing = await repo.GetByIdAsync(eventId, ct);
                if (existing is null || existing.SiteId != siteId)
                    return ErrorResponses.NotFound("AvailabilityEvent", eventId);
                var scope = await repo.AddScopeAsync(eventId, request, ct);
                return Results.Created($"/api/sites/{siteId}/availability-events/{eventId}/scopes/{scope.Id}", scope);
            }, logger, "add event scope", new { siteId, eventId }))
            .RequireAdminAccess()
            .WithName("AddAvailabilityEventScope")
            .WithSummary("Add a scoped override to an availability event");

        events.MapDelete("/{eventId:guid}/scopes/{scopeId:guid}", async (
            Guid siteId,
            Guid eventId,
            Guid scopeId,
            IAvailabilityEventRepository repo,
            CancellationToken ct, ILogger<EndpointLoggerCategory> logger) =>
            await EndpointHelpers.ExecuteAsync(async () =>
            {
                var existing = await repo.GetByIdAsync(eventId, ct);
                if (existing is null || existing.SiteId != siteId)
                    return ErrorResponses.NotFound("AvailabilityEvent", eventId);
                var deleted = await repo.DeleteScopeAsync(eventId, scopeId, ct);
                return deleted ? Results.NoContent() : ErrorResponses.NotFound("Scope", scopeId);
            }, logger, "delete event scope", new { siteId, eventId, scopeId }))
            .RequireAdminAccess()
            .WithName("DeleteAvailabilityEventScope")
            .WithSummary("Remove a scoped override");
    }
}

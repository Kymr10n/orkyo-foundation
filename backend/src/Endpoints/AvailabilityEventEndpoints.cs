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
            .RequireMemberReadEditorWrite();

        events.MapGet("/", async (
            Guid siteId,
            IAvailabilityEventRepository repo,
            CancellationToken ct) =>
            Results.Ok(await repo.GetBySiteAsync(siteId, ct)))
            .WithName("GetAvailabilityEvents")
            .WithSummary("List all availability events for a site");

        events.MapGet("/{eventId:guid}", async (
            Guid siteId,
            Guid eventId,
            IAvailabilityEventRepository repo,
            CancellationToken ct) =>
        {
            return EndpointHelpers.OkOrNotFound(await repo.GetByIdAsync(siteId, eventId, ct), "AvailabilityEvent", eventId);
        })
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
                var result = await repo.UpdateAsync(siteId, eventId, request, ct);
                return EndpointHelpers.OkOrNotFound(result, "AvailabilityEvent", eventId);
            }, logger, "update availability event", new { siteId, eventId }))
            .WithName("UpdateAvailabilityEvent")
            .WithSummary("Update an availability event");

        events.MapDelete("/{eventId:guid}", async (
            Guid siteId,
            Guid eventId,
            IAvailabilityEventRepository repo,
            CancellationToken ct) =>
        {
            return EndpointHelpers.NoContentOrNotFound(await repo.DeleteAsync(siteId, eventId, ct), "AvailabilityEvent", eventId);
        })
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
                var scope = await repo.AddScopeAsync(siteId, eventId, request, ct);
                return scope is null
                    ? ErrorResponses.NotFound("AvailabilityEvent", eventId)
                    : Results.Created($"/api/sites/{siteId}/availability-events/{eventId}/scopes/{scope.Id}", scope);
            }, logger, "add event scope", new { siteId, eventId }))
            .WithName("AddAvailabilityEventScope")
            .WithSummary("Add a scoped override to an availability event");

        events.MapDelete("/{eventId:guid}/scopes/{scopeId:guid}", async (
            Guid siteId,
            Guid eventId,
            Guid scopeId,
            IAvailabilityEventRepository repo,
            CancellationToken ct) =>
        {
            return EndpointHelpers.NoContentOrNotFound(await repo.DeleteScopeAsync(siteId, eventId, scopeId, ct), "Scope", scopeId);
        })
            .WithName("DeleteAvailabilityEventScope")
            .WithSummary("Remove a scoped override");
    }
}

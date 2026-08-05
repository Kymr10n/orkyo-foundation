using Api.Configuration;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orkyo.Shared;

namespace Api.Endpoints;

/// <summary>
/// Read-only iCalendar subscriptions, so a user can see their Orkyo schedule in
/// Outlook, Google Calendar or Apple Calendar and have it stay current without
/// re-exporting anything.
/// </summary>
public static class CalendarFeedEndpoints
{
    public static void MapCalendarFeedEndpoints(this WebApplication app)
    {
        // ── The feed itself ──────────────────────────────────────────────────
        // Anonymous by necessity: a calendar client fetches this unattended and
        // cannot complete an OIDC redirect, so the unguessable token in the path
        // IS the credential. It grants read of one user's schedule and nothing
        // else, and can be revoked on its own.
        app.MapGet("/api/calendar/feed/{token}.ics", [AllowAnonymous] async (
            string token,
            ICalendarFeedTokenRepository tokenRepo,
            ICalendarFeedService feedService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var stored = await tokenRepo.FindActiveByHashAsync(feedService.HashToken(token), ct);
            // Unknown and revoked are the same answer on purpose: a 401 would tell
            // a probing client that some other token exists.
            if (stored is null) return Results.NotFound();

            var events = await feedService.GetEventsAsync(stored.SiteId, DateTime.UtcNow, ct);
            await tokenRepo.TouchAsync(stored.Id, ct);

            var domain = new Uri(configuration.GetRequired(ConfigKeys.AppBaseUrl)).Host;
            var ics = ICalendarWriter.Write(events, stored.Label ?? "Orkyo schedule", domain);

            // A calendar client refetches the whole document; caching it would
            // hand back a stale schedule for as long as the cache lives.
            return Results.Text(ics, "text/calendar; charset=utf-8");
        })
        .WithName("GetCalendarFeed")
        .ExcludeFromDescription();

        // ── Managing subscriptions (authenticated, always the caller's own) ───
        var group = app.MapGroup("/api/calendar/subscriptions").RequireAuthorization();

        group.MapGet("/", async (
            ICalendarFeedTokenRepository tokenRepo,
            ICurrentPrincipal principal,
            CancellationToken ct) =>
        {
            var tokens = await tokenRepo.GetByUserAsync(principal.RequireUserId(), ct);
            // No token value here — it exists in plaintext exactly once, at creation.
            return Results.Ok(tokens);
        })
        .WithName("ListCalendarSubscriptions");

        group.MapPost("/", async (
            CreateCalendarFeedRequest request,
            ICalendarFeedTokenRepository tokenRepo,
            ICalendarFeedService feedService,
            ICurrentPrincipal principal,
            IConfiguration configuration,
            IValidator<CreateCalendarFeedRequest> validator,
            CancellationToken ct) =>
        {
            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var token = feedService.GenerateToken();
                var created = await tokenRepo.CreateAsync(
                    principal.RequireUserId(), feedService.HashToken(token), request.Label, request.SiteId, ct);

                var baseUrl = configuration.GetRequired(ConfigKeys.AppBaseUrl).TrimEnd('/');
                return Results.Ok(new CalendarFeedCreatedResponse
                {
                    Id = created.Id,
                    FeedUrl = $"{baseUrl}/api/calendar/feed/{token}.ics",
                    Label = created.Label,
                    SiteId = created.SiteId,
                });
            });
        })
        .WithName("CreateCalendarSubscription");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ICalendarFeedTokenRepository tokenRepo,
            ICurrentPrincipal principal,
            CancellationToken ct) =>
        {
            var revoked = await tokenRepo.RevokeAsync(id, principal.RequireUserId(), ct);
            return revoked ? Results.NoContent() : Results.NotFound();
        })
        .WithName("RevokeCalendarSubscription");
    }
}

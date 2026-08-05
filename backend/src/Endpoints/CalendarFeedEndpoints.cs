using Api.Configuration;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
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
    /// <summary>
    /// The tenant's own origin, not the configured app base URL. The latter is the apex in
    /// SaaS, which carries no slug for <c>SubdomainResolutionStrategy</c> to resolve — a feed
    /// addressed there is unreachable. Community leaves BaseDomain unset and falls back to it.
    /// </summary>
    private static string TenantOrigin(IConfiguration configuration, ICurrentTenant currentTenant) =>
        TenantHostnamePolicy.BuildOrigin(
            configuration.GetRequired(ConfigKeys.AppBaseUrl),
            configuration[ConfigKeys.TenantResolutionBaseDomain],
            configuration[ConfigKeys.TenantResolutionSubdomainPrefix],
            currentTenant.TenantSlug);

    /// <summary>The same host as <see cref="TenantOrigin"/>, for the <c>.ics</c> PRODID.</summary>
    private static string TenantHost(IConfiguration configuration, ICurrentTenant currentTenant) =>
        TenantHostnamePolicy.BuildHost(
            configuration.GetRequired(ConfigKeys.AppBaseUrl),
            configuration[ConfigKeys.TenantResolutionBaseDomain],
            configuration[ConfigKeys.TenantResolutionSubdomainPrefix],
            currentTenant.TenantSlug);

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
            ICurrentTenant currentTenant,
            IFeatureGate featureGate,
            CancellationToken ct) =>
        {
            // A tenant that drops off a paid plan stops serving its feeds. 404 rather than
            // 402, because this route deliberately gives the same answer to everything it
            // refuses — an upgrade prompt here would confirm the token exists.
            if (!await featureGate.IsEnabledAsync(FeatureKeys.CalendarFeed, ct)) return Results.NotFound();

            var stored = await tokenRepo.FindActiveByHashAsync(feedService.HashToken(token), ct);
            // Unknown and revoked are the same answer on purpose: a 401 would tell
            // a probing client that some other token exists.
            if (stored is null) return Results.NotFound();

            var events = await feedService.GetEventsAsync(stored.SiteId, DateTime.UtcNow, ct);
            await tokenRepo.TouchAsync(stored.Id, ct);

            var domain = TenantHost(configuration, currentTenant);
            var ics = ICalendarWriter.Write(events, stored.Label ?? "Orkyo schedule", domain);

            // A calendar client refetches the whole document; caching it would
            // hand back a stale schedule for as long as the cache lives.
            return Results.Text(ics, "text/calendar; charset=utf-8");
        })
        .WithName("GetCalendarFeed")
        .ExcludeFromDescription();

        // ── Managing subscriptions (authenticated, always the caller's own) ───
        // Listing and revoking stay ungated on purpose: a tenant that loses the
        // entitlement must still be able to see and revoke the tokens it already
        // handed out. Only creating a new one requires the plan.
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
            ICurrentTenant currentTenant,
            IValidator<CreateCalendarFeedRequest> validator,
            IFeatureGate featureGate,
            CancellationToken ct) =>
        {
            // Entitlement, not shape, so it runs ahead of the validator. The dialog
            // turns this into an upgrade prompt rather than a toast.
            if (!await featureGate.IsEnabledAsync(FeatureKeys.CalendarFeed, ct))
                return ErrorResponses.UpgradeRequired("Calendar subscriptions require a paid plan.");

            return await EndpointHelpers.ExecuteAsync(request, validator, async () =>
            {
                var token = feedService.GenerateToken();
                var created = await tokenRepo.CreateAsync(
                    principal.RequireUserId(), feedService.HashToken(token), request.Label, request.SiteId, ct);

                var baseUrl = TenantOrigin(configuration, currentTenant).TrimEnd('/');
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

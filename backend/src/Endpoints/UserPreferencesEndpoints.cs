using System.Text.Json;
using Api.Helpers;
using Api.Middleware;
using Api.Repositories;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class UserPreferencesEndpoints
{
    public static void MapUserPreferencesEndpoints(this WebApplication app)
    {
        // Preferences live in the TENANT database, so the caller must be a member of the tenant
        // the request resolves to. Without the gate a signed-in user could read and write their
        // own preferences row in any tenant's database by naming its slug (saas#296): no
        // other person's data, but a write into a database they have no membership in.
        var prefs = app.MapGroup("/api/preferences")
            .RequireAuthorization()
            .RequireTenantMembership()
            .WithTags("User Preferences");

        prefs.MapGet("/", async (ICurrentPrincipal currentPrincipal, IUserPreferencesRepository repo, CancellationToken ct) =>
        {
            // The repository hands over ownership of the JsonDocument, so it must be disposed here
            // to return its pooled buffer. Clone() is load-bearing, not defensive: an IResult
            // serializes AFTER this handler returns, so returning the document itself under a
            // `using` disposes it before the body is written — verified, that yields a 500.
            using var preferences = await repo.GetPreferencesAsync(currentPrincipal.UserId, ct);
            return preferences is null ? Results.Ok(new { }) : Results.Ok(preferences.RootElement.Clone());
        })
        .WithName("GetUserPreferences")
        .WithSummary("Get current user preferences");

        prefs.MapPut("/", async (ICurrentPrincipal currentPrincipal, JsonDocument body, IUserPreferencesRepository repo, CancellationToken ct) =>
        {
            var success = await repo.UpdatePreferencesAsync(currentPrincipal.UserId, body, ct);
            // 200 + { message } is the sibling success shape for command endpoints (SecurityEndpoints
            // et al.) and what the frontend api client JSON-parses — do not switch to 204.
            return success
                ? Results.Ok(new { message = "Preferences updated successfully" })
                : ErrorResponses.NotFound("Preferences");
        })
        .WithName("UpdateUserPreferences")
        .WithSummary("Update current user preferences");
    }
}

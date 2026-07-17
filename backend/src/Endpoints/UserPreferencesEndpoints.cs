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
        var prefs = app.MapGroup("/api/preferences")
            .RequireAuthorization()
            .WithTags("User Preferences");

        prefs.MapGet("/", async (ICurrentPrincipal currentPrincipal, IUserPreferencesRepository repo, CancellationToken ct) =>
        {
            var preferences = await repo.GetPreferencesAsync(currentPrincipal.UserId, ct);
            return preferences == null ? Results.Ok(new { }) : Results.Ok(preferences);
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

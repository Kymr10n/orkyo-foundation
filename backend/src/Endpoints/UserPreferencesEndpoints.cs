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

        prefs.MapGet("/", async (ICurrentPrincipal currentPrincipal, IUserPreferencesRepository repo, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!currentPrincipal.IsAuthenticated) return Results.Unauthorized();
                var preferences = await repo.GetPreferencesAsync(currentPrincipal.UserId);
                return preferences == null ? Results.Ok(new { }) : Results.Ok(preferences);
            }, logger, "get user preferences");
        })
        .WithName("GetUserPreferences")
        .WithSummary("Get current user preferences");

        prefs.MapPut("/", async (ICurrentPrincipal currentPrincipal, JsonDocument body, IUserPreferencesRepository repo, ILogger<EndpointLoggerCategory> logger) =>
        {
            return await EndpointHelpers.ExecuteAsync(async () =>
            {
                if (!currentPrincipal.IsAuthenticated) return Results.Unauthorized();
                var success = await repo.UpdatePreferencesAsync(currentPrincipal.UserId, body);
                return success ? Results.Ok(new { message = "Preferences updated successfully" }) : Results.Problem("Failed to update preferences");
            }, logger, "update user preferences");
        })
        .WithName("UpdateUserPreferences")
        .WithSummary("Update current user preferences");
    }
}

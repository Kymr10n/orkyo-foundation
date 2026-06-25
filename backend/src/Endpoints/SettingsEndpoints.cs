using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        // Member-read / Admin-write: tenant settings (scheduling config, working hours,
        // public-holiday region, …) are read app-wide — e.g. the auto-schedule flow reads them
        // via useTenantSettings — but only admins manage them (the Administration → Configuration
        // tab). Reads stay member-open; PUT/DELETE require Admin.
        var settings = app.MapGroup("/api/settings")
            .RequireAuthorization()
            .RequireMemberReadAdminWrite()
            .WithTags("Settings")
            .WithMetadata(new SkipTenantResolutionAttribute());

        settings.MapGet("/", async (ITenantSettingsService settingsService, CancellationToken ct) =>
        {
            var current = await settingsService.GetSettingsAsync(ct);
            var descriptors = settingsService.GetDescriptors();
            var result = descriptors.Select(d => new
            {
                d.Key,
                d.Category,
                d.DisplayName,
                d.Description,
                d.ValueType,
                d.DefaultValue,
                d.Scope,
                d.MinValue,
                d.MaxValue,
                CurrentValue = GetPropertyValue(current, d.Key)
            });
            return Results.Ok(new { settings = result });
        })
        .WithName("GetSettings")
        .WithDescription("Get all tenant settings with current values and metadata");

        settings.MapPut("/", async (UpdateSettingsRequest request, ITenantSettingsService settingsService, CancellationToken ct) =>
        {
            if (request.Settings == null || request.Settings.Count == 0)
                return ErrorResponses.BadRequest("At least one setting is required");
            var updated = await settingsService.UpdateSettingsAsync(request.Settings, ct);
            var descriptors = settingsService.GetDescriptors();
            var result = descriptors.Select(d => new
            {
                d.Key,
                d.Category,
                d.DisplayName,
                d.Description,
                d.ValueType,
                d.DefaultValue,
                d.Scope,
                d.MinValue,
                d.MaxValue,
                CurrentValue = GetPropertyValue(updated, d.Key)
            });
            return Results.Ok(new { settings = result });
        })
        .WithName("UpdateSettings")
        .WithDescription("Update one or more tenant settings")
        .Accepts<UpdateSettingsRequest>("application/json");

        settings.MapDelete("/{key}", async (string key, ITenantSettingsService settingsService, CancellationToken ct) =>
        {
            var allDescriptors = TenantSettingsService.GetAllDescriptors();
            var descriptor = allDescriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
            if (descriptor == null) return ErrorResponses.NotFoundMessage($"Unknown setting key: '{key}'");
            var deleted = await settingsService.ResetSettingAsync(key, ct);
            return deleted
                ? Results.Ok(new { message = $"Setting '{key}' reset to default" })
                : ErrorResponses.NotFoundMessage($"Setting '{key}' has no override to reset");
        })
        .WithName("ResetSetting")
        .WithDescription("Reset a single setting to its compiled default");
    }

    private static readonly Dictionary<string, System.Reflection.PropertyInfo> _keyToPropertyMap =
        typeof(TenantSettings)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .ToDictionary(p => TenantSettingsKeyPolicy.PropertyToKey(p.Name), p => p);

    private static string GetPropertyValue(TenantSettings settings, string key)
    {
        if (!_keyToPropertyMap.TryGetValue(key, out var prop)) return "";
        return prop.GetValue(settings)?.ToString() ?? "";
    }
}

public record UpdateSettingsRequest(Dictionary<string, string> Settings);

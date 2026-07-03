using Api.Constants;
using Api.Helpers;
using Api.Middleware;
using Api.Models;
using Api.Security;
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
        // via useTenantSettings — but only admins manage them. Reads stay member-open; PUT/DELETE
        // require Admin. This is the TENANT surface — always invoked with a tenant resolved (no
        // SkipTenantResolution: a tenant is required). Site-scoped platform settings live on the
        // separate site-admin surface at /api/admin/configuration (RequireSiteAdmin), which shares
        // the handlers in SettingsEndpointHandlers — the same ITenantSettingsService serves either
        // scope from the ambient context.
        var settings = app.MapGroup("/api/settings")
            .RequireAuthorization()
            .RequireMemberReadAdminWrite()
            .WithTags("Settings");

        settings.MapGet("/", SettingsEndpointHandlers.GetSettings)
            .WithName("GetSettings")
            .WithDescription("Get all tenant settings with current values and metadata");

        // Tenant PUT/DELETE wrap the shared handler with a tenant-context audit write. The
        // site-admin /api/admin/configuration surface reuses SettingsEndpointHandlers directly and
        // is intentionally NOT audited into the tenant trail (platform scope, not tenant scope).
        settings.MapPut("/", TenantSettingsWrites.UpdateSettings)
            .WithName("UpdateSettings")
            .WithDescription("Update one or more tenant settings")
            .Accepts<UpdateSettingsRequest>("application/json");

        settings.MapDelete("/{key}", TenantSettingsWrites.ResetSetting)
            .WithName("ResetSetting")
            .WithDescription("Reset a single setting to its compiled default");
    }
}

/// <summary>
/// Tenant-surface settings writes — identical to <see cref="SettingsEndpointHandlers"/> but, on
/// success, record a tenant-context audit event (<c>settings.updated</c> / <c>settings.reset</c>)
/// into the tenant's own database. Kept separate so the shared site-admin configuration surface
/// stays audit-free.
/// </summary>
internal static class TenantSettingsWrites
{
    public static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest request, HttpContext ctx, ICurrentPrincipal principal,
        ITenantSettingsService settingsService, ITenantUserService tenantAudit, CancellationToken ct)
    {
        if (request.Settings == null || request.Settings.Count == 0)
            return ErrorResponses.BadRequest("At least one setting is required");
        var updated = await settingsService.UpdateSettingsAsync(request.Settings, ct);
        await tenantAudit.RecordAuditEventAsync(
            ctx.GetOrgContext(), TenantAuditActions.SettingsUpdated, principal.UserId, "settings", null,
            new { keys = request.Settings.Keys.ToArray() }, ct);
        return Results.Ok(new { settings = SettingsEndpointHandlers.Project(settingsService, updated) });
    }

    public static async Task<IResult> ResetSetting(
        string key, HttpContext ctx, ICurrentPrincipal principal,
        ITenantSettingsService settingsService, ITenantUserService tenantAudit, CancellationToken ct)
    {
        var descriptor = TenantSettingsService.GetAllDescriptors()
            .FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null) return ErrorResponses.NotFoundMessage($"Unknown setting key: '{key}'");
        var deleted = await settingsService.ResetSettingAsync(key, ct);
        if (deleted)
            await tenantAudit.RecordAuditEventAsync(
                ctx.GetOrgContext(), TenantAuditActions.SettingsReset, principal.UserId, "settings", key, null, ct);
        return deleted
            ? Results.Ok(new { message = $"Setting '{key}' reset to default" })
            : ErrorResponses.NotFoundMessage($"Setting '{key}' has no override to reset");
    }
}

/// <summary>
/// Shared request handlers for the descriptor-based settings surface. Called by both
/// <c>/api/settings</c> (tenant, member-read/admin-write) and <c>/api/admin/configuration</c>
/// (site-admin control plane). Scope (site vs tenant) is resolved by <see cref="ITenantSettingsService"/>
/// from the ambient tenant context, so the handlers are identical for both surfaces.
/// </summary>
internal static class SettingsEndpointHandlers
{
    public static async Task<IResult> GetSettings(ITenantSettingsService settingsService, CancellationToken ct)
    {
        var current = await settingsService.GetSettingsAsync(ct);
        return Results.Ok(new { settings = Project(settingsService, current) });
    }

    public static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest request, ITenantSettingsService settingsService, CancellationToken ct)
    {
        if (request.Settings == null || request.Settings.Count == 0)
            return ErrorResponses.BadRequest("At least one setting is required");
        var updated = await settingsService.UpdateSettingsAsync(request.Settings, ct);
        return Results.Ok(new { settings = Project(settingsService, updated) });
    }

    public static async Task<IResult> ResetSetting(
        string key, ITenantSettingsService settingsService, CancellationToken ct)
    {
        var allDescriptors = TenantSettingsService.GetAllDescriptors();
        var descriptor = allDescriptors.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
        if (descriptor == null) return ErrorResponses.NotFoundMessage($"Unknown setting key: '{key}'");
        var deleted = await settingsService.ResetSettingAsync(key, ct);
        return deleted
            ? Results.Ok(new { message = $"Setting '{key}' reset to default" })
            : ErrorResponses.NotFoundMessage($"Setting '{key}' has no override to reset");
    }

    internal static IEnumerable<object> Project(ITenantSettingsService settingsService, TenantSettings values) =>
        settingsService.GetDescriptors().Select(d => new
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
            CurrentValue = GetPropertyValue(values, d.Key)
        });

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

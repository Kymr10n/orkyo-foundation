using Api.Configuration;
using Api.Middleware;
using Api.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints.Admin;

/// <summary>
/// Site-admin control-plane surface for the descriptor-based configuration settings (the site-scoped
/// overrides stored in <c>control_plane</c>). Mirrors the other /api/admin tabs — Users, Settings,
/// Diagnostics — gated by <c>RequireSiteAdmin</c> with tenant resolution skipped. Shares its handlers
/// with the tenant <c>/api/settings</c> surface; <see cref="Api.Services.ITenantSettingsService"/>
/// serves site scope automatically when no tenant is resolved (its IsSiteContext branch).
/// </summary>
public static class ConfigurationAdminEndpoints
{
    public static void MapConfigurationAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/configuration")
            .RequireAuthorization()
            .RequireRateLimiting(FoundationRateLimitPolicies.AdminOperations)
            .WithTags("Admin")
            .WithMetadata(new SkipTenantResolutionAttribute());

        group.MapGet("/", SettingsEndpointHandlers.GetSettings)
            .RequireSiteAdmin()
            .WithName("AdminGetConfiguration")
            .WithSummary("Get site-scoped platform settings with current values and metadata");

        group.MapPut("/", SettingsEndpointHandlers.UpdateSettings)
            .RequireSiteAdmin()
            .WithName("AdminUpdateConfiguration")
            .WithSummary("Update one or more site-scoped platform settings")
            .Accepts<UpdateSettingsRequest>("application/json");

        group.MapDelete("/{key}", SettingsEndpointHandlers.ResetSetting)
            .RequireSiteAdmin()
            .WithName("AdminResetConfiguration")
            .WithSummary("Reset a site-scoped setting to its compiled default");
    }
}

using Api.Helpers;
using Api.Middleware;
using Api.Repositories;
using Api.Security;
using Api.Security.Features;
using Api.Security.Quotas;
using Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.Endpoints;

public static class QuotaEndpoints
{
    public static void MapQuotaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings/quotas")
            .RequireAuthorization()
            .RequireTenantMembership()
            .WithTags("Quotas");

        group.MapGet("/", GetQuotas)
            .WithName("GetTenantQuotas")
            .WithSummary("Get live quota usage and limits for the current tenant");
    }

    private static async Task<IResult> GetQuotas(
        ICurrentTenant currentTenant,
        IAuthorizationContext authContext,
        IDbConnectionFactory db,
        ISiteRepository siteRepository,
        ISpaceRepository spaceRepository,
        IAssetRepository assetRepository,
        IQuotaEnforcer quotaEnforcer,
        IFeatureGate featureGate,
        ILogger<EndpointLoggerCategory> logger,
        CancellationToken ct)
    {
        return await EndpointHelpers.ExecuteAsync(async () =>
        {
            authContext.RequireRole(TenantRole.Admin);

            var tenantId = currentTenant.TenantId;

            // ── Live usage ────────────────────────────────────────────────────────
            long activeSeats;
            await using (var conn = db.CreateControlPlaneConnection())
            {
                await conn.OpenAsync(ct);
                await using var cmd = new Npgsql.NpgsqlCommand(
                    "SELECT COUNT(*)::bigint FROM tenant_memberships WHERE tenant_id = @tid AND status = 'active'",
                    conn);
                cmd.Parameters.AddWithValue("tid", tenantId);
                activeSeats = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0L);
            }

            var sitesUsed = (long)await siteRepository.GetEstimatedCountAsync(ct);
            var spacesUsed = (long)await spaceRepository.GetEstimatedCountAsync(ct);
            var storageUsed = await assetRepository.GetTotalSizeBytesAsync(tenantId, ct);

            // ── Limits (edition-specific via IQuotaEnforcer / IFeatureGate) ───────
            var seatsLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.ActiveSeats, ct);
            var sitesLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.ProductionSites, ct);
            var spacesLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.Spaces, ct);
            var storageLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.StorageBytes, ct);

            var apiAccessEnabled = await featureGate.IsEnabledAsync(FeatureKeys.ApiAccess, ct);
            var auditLogEnabled = await featureGate.IsEnabledAsync(FeatureKeys.AuditLog, ct);
            var automatedBackupsEnabled = await featureGate.IsEnabledAsync(FeatureKeys.AutomatedBackups, ct);
            var dataExportEnabled = await featureGate.IsEnabledAsync(FeatureKeys.DataExport, ct);

            return Results.Ok(new
            {
                quotas = new object[]
                {
                    NumericQuota(QuotaResourceTypes.ActiveSeats, "count", seatsLimit, activeSeats),
                    NumericQuota(QuotaResourceTypes.ProductionSites, "count", sitesLimit, sitesUsed),
                    NumericQuota(QuotaResourceTypes.Spaces, "count", spacesLimit, spacesUsed),
                    NumericQuota(QuotaResourceTypes.StorageBytes, "bytes", storageLimit, storageUsed),
                },
                entitlements = new object[]
                {
                    BooleanQuota(FeatureKeys.ApiAccess, apiAccessEnabled),
                    BooleanQuota(FeatureKeys.AuditLog, auditLogEnabled),
                    BooleanQuota(FeatureKeys.AutomatedBackups, automatedBackupsEnabled),
                    BooleanQuota(FeatureKeys.DataExport, dataExportEnabled),
                }
            });
        }, logger, "get tenant quotas");
    }

    private static object NumericQuota(string key, string unit, long limit, long used) => new
    {
        key,
        unit,
        limit,
        used,
        unlimited = limit < 0,
        percentUsed = limit > 0 ? (double)used / limit * 100 : 0.0,
    };

    private static object BooleanQuota(string key, bool enabled) => new { key, enabled };
}

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
            .RequireAdminArea()
            .WithTags("Quotas");

        group.MapGet("/", GetQuotas)
            .WithName("GetTenantQuotas")
            .WithSummary("Get live quota usage and limits for the current tenant");
    }

    private static async Task<IResult> GetQuotas(
        ICurrentTenant currentTenant,
        IDbConnectionFactory db,
        ISiteRepository siteRepository,
        IResourceRepository resourceRepository,
        IAssetRepository assetRepository,
        IQuotaEnforcer quotaEnforcer,
        IFeatureGate featureGate,
        CancellationToken ct)
    {
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
        var spacesUsed = (long)await resourceRepository.GetPlaceableCountAsync(ct);
        var storageUsed = await assetRepository.GetTotalSizeBytesAsync(tenantId, ct);

        // ── Limits (edition-specific via IQuotaEnforcer / IFeatureGate) ───────
        var seatsLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.ActiveSeats, ct);
        var sitesLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.ProductionSites, ct);
        var spacesLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.Spaces, ct);
        var storageLimit = await quotaEnforcer.GetLimitAsync(QuotaResourceTypes.StorageBytes, ct);

        // One list of enforced features (FeatureKeys.Enforced) so this endpoint and the session
        // bootstrap can never disagree about which features exist.
        var entitlements = new List<object>(FeatureKeys.Enforced.Count);
        foreach (var key in FeatureKeys.Enforced)
            entitlements.Add(BooleanQuota(key, await featureGate.IsEnabledAsync(key, ct)));

        return Results.Ok(new
        {
            quotas = new object[]
            {
                NumericQuota(QuotaResourceTypes.ActiveSeats, "count", seatsLimit, activeSeats),
                NumericQuota(QuotaResourceTypes.ProductionSites, "count", sitesLimit, sitesUsed),
                NumericQuota(QuotaResourceTypes.Spaces, "count", spacesLimit, spacesUsed),
                NumericQuota(QuotaResourceTypes.StorageBytes, "bytes", storageLimit, storageUsed),
            },
            entitlements
        });
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

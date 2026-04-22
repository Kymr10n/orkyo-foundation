using Orkyo.Shared;

namespace Api.Services;

public static class TenantResolverQueryContract
{
    public const string SlugParameterName = "slug";

    // Selected column order is part of the cross-layer contract used by resolver mapping.
    public const int TenantIdOrdinal = 0;
    public const int TenantSlugOrdinal = 1;
    public const int DbIdentifierOrdinal = 2;
    public const int StatusOrdinal = 3;
    public const int TierOrdinal = 4;
    public const int SuspensionReasonOrdinal = 5;

    public static string BuildSelectBySlugSql()
    {
        return $"SELECT id, slug, db_identifier, status, tier, suspension_reason FROM tenants WHERE slug = @{SlugParameterName} AND status != '{TenantStatusConstants.Deleting}'";
    }
}
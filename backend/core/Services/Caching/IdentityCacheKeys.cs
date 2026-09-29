namespace Api.Services.Caching;

/// <summary>
/// Keys of the identity entries <c>ContextEnrichmentMiddleware</c> keeps on the shared
/// <see cref="SingleFlightCache"/>, in one place so a membership write can evict exactly the
/// entry the middleware reads.
/// </summary>
public static class IdentityCacheKeys
{
    /// <summary>A user's cached role in one tenant (a <c>TenantRole</c>).</summary>
    public static string Role(Guid userId, Guid tenantId) => $"identity:role:{userId}:{tenantId}";
}

namespace Api.Services.Caching;

/// <summary>
/// Keys of the identity entries <c>ContextEnrichmentMiddleware</c> keeps on the shared
/// <see cref="SingleFlightCache"/>, in one place so a membership write can evict exactly the
/// entry the middleware reads.
/// </summary>
public static class IdentityCacheKeys
{
    /// <summary>Every identity key starts with this; the eviction channel accepts nothing else.</summary>
    public const string Prefix = "identity:";

    /// <summary>The DB principal of an external (Keycloak) subject (a <c>PrincipalContext</c>).</summary>
    public static string Principal(string externalSubject) => $"{Prefix}principal:{externalSubject}";

    /// <summary>A user's cached role in one tenant (a <c>TenantRole</c>).</summary>
    public static string Role(Guid userId, Guid tenantId) => $"{Prefix}role:{userId}:{tenantId}";
}

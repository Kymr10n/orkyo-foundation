using Api.Models;

namespace Api.Services;

/// <summary>
/// Extension methods bridging foundation <see cref="TenantContext"/> to foundation <see cref="OrgContext"/>.
/// </summary>
public static class OrgContextExtensions
{
    /// <summary>
    /// Build an <see cref="OrgContext"/> from a <see cref="TenantContext"/> (multi-tenant mode).
    /// </summary>
    public static OrgContext FromTenant(TenantContext tenant) => new()
    {
        OrgId = tenant.TenantId,
        OrgSlug = tenant.TenantSlug,
        DbConnectionString = tenant.TenantDbConnectionString
    };
}

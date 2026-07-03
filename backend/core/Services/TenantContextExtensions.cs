using Api.Security;

namespace Api.Services;

/// <summary>
/// Maps a resolved tenant identity to the domain-facing <see cref="OrgContext"/> that repositories
/// and services consume. Centralizes the (OrgId, OrgSlug, DbConnectionString) shape so it lives in
/// one place if it ever changes.
/// </summary>
public static class TenantContextExtensions
{
    public static OrgContext ToOrgContext(this TenantContext tenant) => new()
    {
        OrgId = tenant.TenantId,
        OrgSlug = tenant.TenantSlug,
        DbConnectionString = tenant.TenantDbConnectionString,
    };

    public static OrgContext ToOrgContext(this ICurrentTenant tenant) => new()
    {
        OrgId = tenant.RequireTenantId(),
        OrgSlug = tenant.TenantSlug,
        DbConnectionString = tenant.TenantDbConnectionString,
    };
}

namespace Api.Services;

public static class TenantSlugSelectionPolicy
{
    public static string? SelectSlug(string? subdomain, string? tenantHeader)
    {
        // Prefer subdomain, then header fallback for internal/system calls.
        var tenantSlug = subdomain ?? tenantHeader;
        return string.IsNullOrWhiteSpace(tenantSlug) ? null : tenantSlug;
    }
}
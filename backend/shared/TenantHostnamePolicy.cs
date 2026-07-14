namespace Orkyo.Shared;

/// <summary>
/// Single source of truth for the tenant subdomain shape. Shared (not Web) so the
/// SaaS worker can build workspace links in lifecycle emails without referencing
/// ASP.NET Core; <c>TenantMiddlewareOptions.BuildTenantHostname</c> delegates here.
/// </summary>
public static class TenantHostnamePolicy
{
    /// <summary>
    /// Build the full hostname for a tenant subdomain, or null when
    /// <paramref name="baseDomain"/> is not configured (host-based resolution disabled).
    /// <para>
    /// Production: <c>{slug}.orkyo.com</c><br/>
    /// Staging:    <c>staging-{slug}.orkyo.com</c> (subdomainPrefix = "staging-")
    /// </para>
    /// </summary>
    public static string? BuildHostname(string? baseDomain, string? subdomainPrefix, string slug)
    {
        if (string.IsNullOrEmpty(baseDomain)) return null;
        var prefix = subdomainPrefix ?? "";
        return $"{prefix}{slug}.{baseDomain}";
    }
}

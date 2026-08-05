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
    /// <exception cref="ArgumentException">
    /// <paramref name="slug"/> is empty. <c>ICurrentTenant.TenantSlug</c> reads as an empty
    /// string when no tenant was resolved, and quietly emitting <c>.orkyo.com</c> for it would
    /// hand out an address that resolves to nothing.
    /// </exception>
    public static string? BuildHostname(string? baseDomain, string? subdomainPrefix, string slug)
    {
        if (string.IsNullOrEmpty(baseDomain)) return null;
        if (string.IsNullOrEmpty(slug))
            throw new ArgumentException("A tenant slug is required to build a tenant hostname", nameof(slug));
        var prefix = subdomainPrefix ?? "";
        return $"{prefix}{slug}.{baseDomain}";
    }

    /// <summary>
    /// The host a tenant is reachable at: its own subdomain when host-based resolution is
    /// configured, otherwise the host of <paramref name="appBaseUrl"/> (single-tenant
    /// community, local dev).
    /// </summary>
    public static string BuildHost(string appBaseUrl, string? baseDomain, string? subdomainPrefix, string slug)
        => BuildHostname(baseDomain, subdomainPrefix, slug)
           ?? new Uri(appBaseUrl, UriKind.Absolute).Host;

    /// <summary>
    /// The origin (<c>scheme://host</c>) a tenant is reachable at: its own subdomain when
    /// host-based resolution is configured, otherwise <paramref name="appBaseUrl"/> unchanged
    /// (single-tenant community, local dev). The scheme is taken from <paramref name="appBaseUrl"/>.
    /// <para>
    /// Anything handing a tenant an absolute URL to itself must go through here: the apex
    /// carries no slug, so <c>SubdomainResolutionStrategy</c> cannot resolve a tenant from it.
    /// </para>
    /// </summary>
    public static string BuildOrigin(string appBaseUrl, string? baseDomain, string? subdomainPrefix, string slug)
    {
        var hostname = BuildHostname(baseDomain, subdomainPrefix, slug);
        if (hostname is null) return appBaseUrl;

        var scheme = new Uri(appBaseUrl, UriKind.Absolute).Scheme;
        return $"{scheme}://{hostname}";
    }
}

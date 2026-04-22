namespace Api.Middleware;

public class TenantMiddlewareOptions
{
    /// <summary>
    /// The base domain for host-based tenant resolution (e.g., "orkyo.app").
    /// Tenants are resolved from subdomains: {tenant}.orkyo.app
    /// Leave null to disable host-based resolution and rely on X-Tenant-Slug header.
    /// </summary>
    public string? BaseDomain { get; set; }

    /// <summary>
    /// Allowed hosts for local development that use simple subdomain format.
    /// e.g., ["localhost", "127.0.0.1"] allows "demo.localhost:3000"
    /// </summary>
    public string[] LocalHosts { get; set; } = ["localhost", "127.0.0.1"];

    /// <summary>
    /// Optional prefix stripped from the subdomain before tenant lookup.
    /// Used in staging to flatten nested subdomains into the wildcard cert range.
    /// e.g., SubdomainPrefix="staging-" makes staging-acme.orkyo.com resolve tenant "acme".
    /// </summary>
    public string? SubdomainPrefix { get; set; }

    /// <summary>
    /// Whether to allow X-Tenant-Slug header and ?tenant query parameter.
    /// Must be set to true in development/testing via TenantResolution:AllowTenantHeader=true.
    /// Default: false — production enforces host-based resolution only.
    /// </summary>
    public bool AllowTenantHeader { get; set; } = false;

    /// <summary>
    /// Build the full hostname for a tenant subdomain.
    /// Returns null when <see cref="BaseDomain"/> is not configured.
    /// <para>
    /// Production: <c>{slug}.orkyo.com</c><br/>
    /// Staging:    <c>staging-{slug}.orkyo.com</c> (SubdomainPrefix = "staging-")
    /// </para>
    /// </summary>
    public string? BuildTenantHostname(string slug)
    {
        if (string.IsNullOrEmpty(BaseDomain)) return null;
        var prefix = SubdomainPrefix ?? "";
        return $"{prefix}{slug}.{BaseDomain}";
    }

    /// <summary>
    /// Extract the tenant slug from a hostname, stripping port and the environment prefix.
    /// Returns null when the hostname is the apex, doesn't match <see cref="BaseDomain"/>,
    /// has nested subdomains, or the prefix doesn't match.
    /// </summary>
    public string? ExtractSlugFromHost(string host)
    {
        if (string.IsNullOrEmpty(BaseDomain)) return null;

        var hostWithoutPort = host.Split(':')[0].ToLowerInvariant();
        var baseDomain = BaseDomain.ToLowerInvariant();

        if (!hostWithoutPort.EndsWith($".{baseDomain}"))
            return null;

        var subdomain = hostWithoutPort[..^(baseDomain.Length + 1)];
        if (subdomain.Contains('.')) return null;

        if (!string.IsNullOrEmpty(SubdomainPrefix))
        {
            var prefix = SubdomainPrefix.ToLowerInvariant();
            return subdomain.StartsWith(prefix) ? subdomain[prefix.Length..] : null;
        }

        return subdomain;
    }
}
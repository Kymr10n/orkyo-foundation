using Api.Constants;
using Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace Api.Services;

/// <summary>
/// Resolves the tenant slug from the HTTP request using subdomain extraction,
/// with optional fallback to X-Tenant-Slug header and ?tenant query parameter.
/// Used in multi-tenant (SaaS) mode.
/// </summary>
public class SubdomainResolutionStrategy : ITenantResolutionStrategy
{
    private readonly TenantMiddlewareOptions _options;

    public SubdomainResolutionStrategy(TenantMiddlewareOptions options)
    {
        _options = options;
    }

    public string? ResolveSlug(HttpContext context)
    {
        var subdomain = ExtractSubdomain(context.Request.Host.Host);

        string? header = null;
        string? query = null;

        if (_options.AllowTenantHeader)
        {
            header = context.Request.Headers[HeaderConstants.TenantSlug].FirstOrDefault();
            query = context.Request.Query["tenant"].FirstOrDefault();
        }

        return subdomain ?? header ?? query;
    }

    private string? ExtractSubdomain(string host)
    {
        // Production / staging: delegate to the shared options helper
        var slug = _options.ExtractSlugFromHost(host);
        if (slug != null) return slug;

        var hostWithoutPort = host.Split(':')[0].ToLowerInvariant();

        // If baseDomain is configured and matched (even as apex), don't fall through to localhost
        if (!string.IsNullOrEmpty(_options.BaseDomain))
        {
            var baseDomain = _options.BaseDomain.ToLowerInvariant();
            if (hostWithoutPort == baseDomain || hostWithoutPort.EndsWith($".{baseDomain}"))
                return null;
        }

        // Check for local development hosts (e.g., demo.localhost)
        foreach (var localHost in _options.LocalHosts)
        {
            var localHostLower = localHost.ToLowerInvariant();
            if (hostWithoutPort.EndsWith($".{localHostLower}"))
            {
                var subdomain = hostWithoutPort[..^(localHostLower.Length + 1)];
                if (!subdomain.Contains('.'))
                {
                    return subdomain;
                }
            }
        }

        return null;
    }
}
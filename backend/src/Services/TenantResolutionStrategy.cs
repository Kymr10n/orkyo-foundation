using Microsoft.AspNetCore.Http;

namespace Api.Services;

/// <summary>
/// Extracts a tenant slug from an HTTP request.
/// Implementations determine HOW the slug is resolved — subdomain parsing (SaaS),
/// fixed config (single-org), etc. The middleware determines WHAT to do with the slug.
/// </summary>
public interface ITenantResolutionStrategy
{
    /// <summary>
    /// Resolve the tenant slug from the current request.
    /// Returns null when no slug can be determined.
    /// </summary>
    string? ResolveSlug(HttpContext context);
}
namespace Api.Constants;

/// <summary>
/// HTTP header name constants.
/// </summary>
public static class HeaderConstants
{
    /// <summary>Tenant slug override header (development only)</summary>
    public const string TenantSlug = "X-Tenant-Slug";

    /// <summary>Correlation ID for end-to-end request tracing</summary>
    public const string CorrelationId = "X-Correlation-ID";

    /// <summary>CSRF token header for BFF cookie authentication</summary>
    public const string CsrfToken = "X-CSRF-Token";

    /// <summary>Standard HTTP Authorization request header</summary>
    public const string Authorization = "Authorization";

    // Security response headers (set by SecurityHeadersMiddleware, asserted in tests)
    public const string XContentTypeOptions = "X-Content-Type-Options";
    public const string XFrameOptions = "X-Frame-Options";
    public const string ReferrerPolicy = "Referrer-Policy";
    public const string XPermittedCrossDomainPolicies = "X-Permitted-Cross-Domain-Policies";
    public const string ContentSecurityPolicy = "Content-Security-Policy";
    public const string PermissionsPolicy = "Permissions-Policy";
    public const string StrictTransportSecurity = "Strict-Transport-Security";
}

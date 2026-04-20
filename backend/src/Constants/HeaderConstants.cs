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
}

using Api.Constants;

namespace Api.Configuration;

/// <summary>
/// Configuration for BFF (Backend-For-Frontend) authentication.
/// Bound from environment variables when <c>BFF_ENABLED=true</c>.
/// </summary>
public sealed class BffOptions
{
    /// <summary>Default session cookie name when BFF_COOKIE_NAME is not configured.</summary>
    public const string DefaultCookieName = "orkyo-session";

    /// <summary>Session cookie name.</summary>
    public string CookieName { get; set; } = DefaultCookieName;

    /// <summary>CSRF double-submit cookie name (NOT HttpOnly — read by JS).</summary>
    public string CsrfCookieName { get; set; } = "orkyo-csrf";

    /// <summary>CSRF header name that must match the cookie value.</summary>
    public string CsrfHeaderName { get; set; } = HeaderConstants.CsrfToken;

    /// <summary>
    /// Cookie domain (e.g. <c>.orkyo.com</c>). Null in dev so cookies
    /// default to the current host.
    /// </summary>
    public string? CookieDomain { get; set; }

    /// <summary>Whether to set the Secure flag on cookies.</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>How long a BFF session lasts before requiring re-auth.</summary>
    public TimeSpan SessionDuration { get; set; } = TimeSpan.FromHours(8);

    /// <summary>
    /// The single OIDC redirect URI registered in Keycloak
    /// (e.g. <c>https://orkyo.com/api/auth/bff/callback</c>).
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Allowed hosts for the <c>returnTo</c> parameter (open-redirect protection).
    /// Supports exact matches and wildcard prefixes (e.g. <c>*.orkyo.com</c>).
    /// </summary>
    public string[] AllowedReturnToHosts { get; set; } = [];

    /// <summary>
    /// The canonical public app origin (<c>APP_BASE_URL</c>), e.g. <c>https://orkyo.com</c>
    /// or <c>http://localhost:5174</c>. Preferred base for default/error redirects because it
    /// carries the PORT, which the host-only <see cref="AllowedReturnToHosts"/> list drops
    /// (the cause of the <c>http://localhost/login</c> dead-end).
    /// </summary>
    public string AppBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// OIDC scopes requested during authorization.
    /// Defaults to <c>openid profile email</c>.
    /// Override via <c>BFF_SCOPES</c> environment variable to add scopes
    /// such as <c>offline_access</c> for refresh token support.
    /// </summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>
    /// Validates that a <c>returnTo</c> URL is safe to redirect to.
    /// </summary>
    public bool IsReturnToAllowed(string returnTo)
    {
        if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var uri))
            return false;

        // Reject non-https in production (CookieSecure=true).
        // Allow http only in dev where CookieSecure=false.
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && !CookieSecure))
            return false;

        var host = uri.Host;
        var hostWithPort = uri.IsDefaultPort ? host : $"{host}:{uri.Port}";

        foreach (var allowed in AllowedReturnToHosts)
        {
            if (allowed.StartsWith("*."))
            {
                // Wildcard: match the suffix (e.g. *.orkyo.com matches demo.orkyo.com and orkyo.com)
                var suffix = allowed[1..]; // ".orkyo.com"
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                    host.Equals(suffix[1..], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
                     hostWithPort.Equals(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The default base URL for building default/error redirects (e.g. the SPA login page).
    /// Prefers <see cref="AppBaseUrl"/> because it carries the port; otherwise derives
    /// scheme + host from <see cref="RedirectUri"/> and <see cref="AllowedReturnToHosts"/>.
    /// Returns null if not derivable.
    /// </summary>
    public string? GetDefaultReturnToBase()
    {
        // Prefer the configured public app origin — it carries the port, which the host-only
        // AllowedReturnToHosts list drops (the cause of the http://localhost/login dead-end).
        if (!string.IsNullOrEmpty(AppBaseUrl))
            return AppBaseUrl.TrimEnd('/');

        // Fallback: derive scheme from the redirect URI (http in dev, https in prod)
        var scheme = "https";
        if (Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirectUri))
            scheme = redirectUri.Scheme;

        // Pick the first non-wildcard allowed host
        var host = AllowedReturnToHosts.FirstOrDefault(h => !h.StartsWith("*"));
        if (string.IsNullOrEmpty(host))
            return null;

        return $"{scheme}://{host}";
    }
}

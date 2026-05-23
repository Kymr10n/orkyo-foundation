namespace Api.Integrations.Keycloak;

/// <summary>
/// Pure policy describing how to forward the public Keycloak hostname/scheme
/// to the internal Keycloak base URL when the API talks to Keycloak through
/// an internal address (e.g. <c>http://keycloak:8080</c>) but tokens carry
/// the public issuer (e.g. <c>https://auth.example.com</c>).
///
/// Without these forwarded headers the token issuer claim mismatches the
/// effective request URL, which Keycloak rejects.
/// </summary>
public static class KeycloakInternalProxyPolicy
{
    /// <summary>
    /// HTTP header name used to forward the original request scheme.
    /// </summary>
    public const string ForwardedProtoHeader = "X-Forwarded-Proto";

    /// <summary>
    /// HTTP header name used to forward the original request host (and
    /// optional port).
    /// </summary>
    public const string ForwardedHostHeader = "X-Forwarded-Host";

    /// <summary>
    /// Returns <c>true</c> when the internal base URL is configured and is
    /// different from the public base URL, indicating that forwarded headers
    /// must be set on outbound admin requests.
    /// </summary>
    public static bool ShouldSetForwardedHeaders(string publicBaseUrl, string? internalBaseUrl)
    {
        if (string.IsNullOrEmpty(internalBaseUrl)) return false;
        return !string.Equals(internalBaseUrl, publicBaseUrl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the value for <see cref="ForwardedHostHeader"/> from the public
    /// base URL.  Default ports are omitted to match standard
    /// <c>X-Forwarded-Host</c> semantics.
    /// </summary>
    public static string BuildForwardedHost(string publicBaseUrl)
    {
        var publicUri = new Uri(publicBaseUrl);
        return publicUri.IsDefaultPort
            ? publicUri.Host
            : $"{publicUri.Host}:{publicUri.Port}";
    }

    /// <summary>
    /// Returns the scheme component of the public base URL for use as the
    /// <see cref="ForwardedProtoHeader"/> value.
    /// </summary>
    public static string BuildForwardedProto(string publicBaseUrl) =>
        new Uri(publicBaseUrl).Scheme;
}

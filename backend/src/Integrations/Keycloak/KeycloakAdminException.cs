using Microsoft.AspNetCore.Http;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Thrown when a Keycloak Admin API call fails.
/// The message is caller-safe (already sanitized for end-user display) and
/// <see cref="StatusCode"/> hints at the HTTP status the endpoint layer should return.
///
/// Lives in <c>orkyo-foundation</c> because the failure contract is identical
/// across multi-tenant SaaS and single-tenant Community deployments; both
/// products surface Keycloak admin failures through the same endpoint-helper
/// mapping pipeline.
/// </summary>
public sealed class KeycloakAdminException : Exception
{
    /// <summary>
    /// Suggested HTTP status for the response. Defaults to 502 Bad Gateway because
    /// most failures are upstream issues with Keycloak itself rather than user input.
    /// </summary>
    public int StatusCode { get; }

    public KeycloakAdminException(string message, int statusCode = StatusCodes.Status502BadGateway, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

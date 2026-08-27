using Api.Constants;
using Api.Helpers;
using Microsoft.AspNetCore.Http;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Maps a <see cref="KeycloakAdminException"/> onto the canonical platform
/// <see cref="Helpers.OrkyoProblemDetails"/> contract used by both products' endpoint pipelines.
///
/// Lives in <c>orkyo-foundation</c> because the failure-to-HTTP shape is
/// identical across multi-tenant SaaS and single-tenant Community deployments;
/// the underlying <see cref="ErrorResponses"/>/<see cref="ErrorCodes"/> contracts
/// are also foundation-owned.
/// </summary>
public static class KeycloakAdminExceptionMapper
{
    /// <summary>Wire constant for the unmapped-status fallback error code.</summary>
    public const string KeycloakErrorCode = "KEYCLOAK_ERROR";

    /// <summary>
    /// Map the exception's <see cref="KeycloakAdminException.StatusCode"/> to a
    /// canonical <see cref="IResult"/>:
    /// <list type="bullet">
    ///   <item>400 → <see cref="ErrorResponses.BadRequest(string, string)"/></item>
    ///   <item>404 → <see cref="ErrorResponses.NotFoundMessage(string)"/></item>
    ///   <item>409 → <see cref="ErrorResponses.Conflict(string)"/></item>
    ///   <item>any other status → problem body with <see cref="KeycloakErrorCode"/> at the same status</item>
    /// </list>
    /// </summary>
    public static IResult Map(KeycloakAdminException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.StatusCode switch
        {
            StatusCodes.Status400BadRequest => ErrorResponses.BadRequest(exception.Message),
            StatusCodes.Status404NotFound => ErrorResponses.NotFoundMessage(exception.Message),
            StatusCodes.Status409Conflict => ErrorResponses.Conflict(exception.Message),
            _ => ProblemResults.Problem(exception.StatusCode, KeycloakErrorCode, exception.Message),
        };
    }
}

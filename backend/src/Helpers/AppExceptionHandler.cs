using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Api.Helpers;

public sealed class AppExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var result = Map(exception);

        if (result is null) return false;

        await result.ExecuteAsync(httpContext);
        return true;
    }

    /// <summary>
    /// The one classification of an exception as a client error, shared with the MCP pipeline:
    /// the detail of the 4xx <see cref="Map"/> answers it with, or null when it is not one (a
    /// programming error, an upstream failure) and the caller must not see its message.
    /// </summary>
    internal static string? ClientErrorDetail(Exception exception)
        => Map(exception) is IStatusCodeHttpResult { StatusCode: >= 400 and < 500 } and IValueHttpResult { Value: OrkyoProblemDetails problem }
            ? problem.Detail ?? problem.Title
            : null;

    /// <summary>The response for an exception this handler owns, or null to let it fall through.</summary>
    private static IResult? Map(Exception exception)
        => exception switch
        {
            // Framework exception: required query/route/body parameter missing from the request.
            // ASP.NET Core would return 400 anyway, but catching it here prevents DeveloperExceptionPageMiddleware
            // from logging it as an unhandled error.
            Microsoft.AspNetCore.Http.BadHttpRequestException bhr
                => ErrorResponses.BadRequest(bhr.Message),
            FeatureNotAvailableException fna
                => ErrorResponses.Forbidden(message: fna.Message),
            AccountLockedException ale
                => ErrorResponses.Forbidden(code: Api.Constants.ApiErrorCodes.AccountLocked, message: ale.Message),
            QuotaExceededException qee
                => ErrorResponses.QuotaExceeded(qee.ResourceType, qee.Limit, qee.Message),
            NotFoundException nfe
                => ErrorResponses.NotFound(nfe.ResourceType.Length > 0 ? nfe.ResourceType : nfe.Message, nfe.ResourceId),
            ConflictException ce
                => ErrorResponses.Conflict(ce.Message),
            CapabilityNotApplicableException cna
                => ErrorResponses.BadRequest(cna.Message),
            // Guard-clause failures (ThrowIfNull etc.) are programming errors, not
            // client errors: their messages carry internal parameter names and were
            // being echoed to clients as 400 bodies (#96). Let them fall through to
            // the framework's generic 500 ProblemDetails, which discloses nothing.
            ArgumentNullException or ArgumentOutOfRangeException
                => null,
            // Plain ArgumentException remains the deliberate boundary-validation
            // signal (~90 endpoint/service sites) whose message IS the user-facing
            // error contract.
            ArgumentException arg
                => ErrorResponses.BadRequest(arg.Message),
            UnauthorizedAccessException
                => ErrorResponses.Forbidden(),
            // A tenant-scoped service resolved where no tenant was resolved: a wiring error,
            // reported as a 500 with a stable code so it never reads as a data problem.
            TenantContextUnavailableException tcu
                => ProblemResults.Problem(StatusCodes.Status500InternalServerError, TenantContextUnavailableException.Code, tcu.Message),
            KeycloakAdminException kae
                => KeycloakAdminExceptionMapper.Map(kae),
            PostgresException pg when pg.SqlState == "23505"
                => ErrorResponses.Conflict("A record with this identifier already exists"),
            // A site's stored scheduling settings name a time zone this runtime cannot
            // resolve. The write path validates against GetSystemTimeZones()
            // (SchedulingValidators), but seeds write scheduling_settings straight to the
            // database and bypass it — so an unresolvable id is a reachable state, not an
            // impossible one. Report it as data the caller can fix, never as a bare 500.
            // A runtime image with no tzdata at all is caught earlier, by
            // ConfigurationValidator.TimeZoneDataError() at startup.
            TimeZoneNotFoundException tznf
                => ErrorResponses.UnprocessableEntity(
                    $"The site's scheduling settings use a time zone this server cannot resolve: {tznf.Message}"),
            _ => null
        };
}

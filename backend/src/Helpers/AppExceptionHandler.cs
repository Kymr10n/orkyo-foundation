using Api.Integrations.Keycloak;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace Api.Helpers;

public sealed class AppExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var result = exception switch
        {
            // Framework exception: required query/route/body parameter missing from the request.
            // ASP.NET Core would return 400 anyway, but catching it here prevents DeveloperExceptionPageMiddleware
            // from logging it as an unhandled error.
            Microsoft.AspNetCore.Http.BadHttpRequestException bhr
                => ErrorResponses.BadRequest(bhr.Message),
            FeatureNotAvailableException fna
                => ErrorResponses.Forbidden(message: fna.Message),
            NotFoundException nfe
                => ErrorResponses.NotFound(nfe.ResourceType.Length > 0 ? nfe.ResourceType : nfe.Message, nfe.ResourceId),
            ConflictException ce
                => ErrorResponses.Conflict(ce.Message),
            KeyNotFoundException knf
                => ErrorResponses.NotFound(knf.Message, Guid.Empty),
            CapabilityNotApplicableException cna
                => ErrorResponses.BadRequest(cna.Message),
            ArgumentException arg
                => ErrorResponses.BadRequest(arg.Message),
            UnauthorizedAccessException
                => ErrorResponses.Forbidden(),
            KeycloakAdminException kae
                => KeycloakAdminExceptionMapper.Map(kae),
            PostgresException pg when pg.SqlState == "23505"
                => ErrorResponses.Conflict("A record with this identifier already exists"),
            _ => null
        };

        if (result is null) return false;

        await result.ExecuteAsync(httpContext);
        return true;
    }
}

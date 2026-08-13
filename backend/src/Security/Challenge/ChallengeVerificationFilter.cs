using Api.Constants;
using Api.Helpers;
using Api.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api.Security.Challenge;

/// <summary>
/// Verifies the bot-challenge token on public anonymous form endpoints.
/// Reads the token from the first handler argument implementing
/// <see cref="IChallengeProtectedRequest"/>; a missing argument is treated as an
/// empty token (a configured key rejects it, the NoOp provider passes it).
/// Fail-open behavior lives in the provider, not here.
/// </summary>
public sealed class ChallengeVerificationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var services = httpContext.RequestServices;
        var challengeProvider = services.GetRequiredService<IChallengeProvider>();
        var clientIpAccessor = services.GetRequiredService<IClientIpAccessor>();

        var token = context.Arguments.OfType<IChallengeProtectedRequest>().FirstOrDefault()?.ChallengeToken ?? "";
        var challenge = await challengeProvider.VerifyAsync(
            token, clientIpAccessor.GetClientIp(httpContext) ?? "", httpContext.RequestAborted);

        if (!challenge.Success)
        {
            services.GetRequiredService<ILogger<ChallengeVerificationFilter>>()
                .LogWarning("Challenge verification failed for {Path}: {Error}",
                    httpContext.Request.Path, challenge.ErrorCode);
            return ProblemResults.Problem(StatusCodes.Status403Forbidden, ErrorCodes.ChallengeFailed,
                detail: "Verification failed. Refresh the page and try again.");
        }

        return await next(context);
    }
}

public static class ChallengeVerificationEndpointExtensions
{
    /// <summary>Runs the bot-challenge check before the handler (and before validation).</summary>
    public static RouteHandlerBuilder RequireChallengeVerification(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<ChallengeVerificationFilter>();
}

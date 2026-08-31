using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.Services.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Reporting.Auth;

/// <summary>
/// Authentication scheme that validates <c>orkyo_rpt_*</c> reporting tokens.
/// Runs only when endpoints explicitly require the "ReportingToken" policy.
/// Does NOT interfere with the default JWT Bearer / BFF cookie auth flow.
/// </summary>
public sealed class ReportingTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ReportingToken";

    /// <summary>Authorization policy name gating reporting endpoints (same literal as the scheme).</summary>
    public const string PolicyName = SchemeName;

    public const string TokenPrefix = "orkyo_rpt_";

    private readonly IReportingTokenService _tokenService;
    private readonly IServiceScopeFactory _scopeFactory;

    public ReportingTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IReportingTokenService tokenService,
        IServiceScopeFactory scopeFactory)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization["Bearer ".Length..].Trim();
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var record = await _tokenService.ValidateAsync(token, Context.RequestAborted);
        if (record is null)
        {
            Logger.LogWarning("Reporting token validation failed for prefix {Prefix}",
                ExtractPrefix(token));
            return AuthenticateResult.Fail("Invalid, expired, or revoked reporting token.");
        }

        // Store token record for downstream filters and audit middleware
        Context.Items[ReportingTokenContextKeys.TokenRecord] = record;

        // Touch last_used_at asynchronously — don't block the request. Through a fresh DI
        // scope, NOT the request's _tokenService: this task outlives the request, whose scope
        // (and its DB connection factory) is disposed the moment the response completes. Using
        // the scoped instance made the update race disposal and silently stop under load.
        _ = TouchLastUsedInOwnScopeAsync(record.Id);

        var claims = new[]
        {
            new Claim(ReportingTokenContextKeys.TokenIdClaim, record.Id.ToString()),
            new Claim(ReportingTokenContextKeys.TenantIdClaim, record.TenantId.ToString()),
            new Claim(ReportingTokenContextKeys.ScopesClaim, record.Scopes),
            new Claim(ReportingTokenContextKeys.TokenPrefixClaim, record.TokenPrefix),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>Background update with its own scope, so it cannot race request disposal.</summary>
    private async Task TouchLastUsedInOwnScopeAsync(Guid tokenId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<IReportingTokenService>()
                .TouchLastUsedAsync(tokenId);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task; an escaped exception would only surface as an
            // unobserved-task event. The touch is best-effort by design.
            Logger.LogWarning(ex, "Background last_used_at update failed for reporting token {TokenId}", tokenId);
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new
        {
            error = "unauthorized",
            message = "Invalid reporting token.",
        });
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        return Response.WriteAsJsonAsync(new
        {
            error = "forbidden",
            message = "The reporting token does not have access to this dataset.",
        });
    }

    private static string? ExtractPrefix(string rawToken)
    {
        // orkyo_rpt_{prefix}_{secret}
        var rest = rawToken[TokenPrefix.Length..];
        var idx = rest.IndexOf('_');
        return idx > 0 ? rest[..idx] : null;
    }
}

public static class ReportingTokenContextKeys
{
    public const string TokenRecord = "ReportingTokenRecord";
    public const string TokenIdClaim = "reporting_token_id";
    public const string TenantIdClaim = "reporting_tenant_id";
    public const string ScopesClaim = "reporting_scopes";
    public const string TokenPrefixClaim = "reporting_token_prefix";
}

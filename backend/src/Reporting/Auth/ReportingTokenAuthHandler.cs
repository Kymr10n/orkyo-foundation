using System.Text.Encodings.Web;
using Api.PlatformApi.Auth;
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
public sealed class ReportingTokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IReportingTokenService tokenService,
    IServiceScopeFactory scopeFactory)
    : BearerTokenAuthHandler<ReportingTokenRecord, IReportingTokenService>(
        options, logger, encoder, tokenService, scopeFactory)
{
    public const string SchemeName = "ReportingToken";

    /// <summary>Authorization policy name gating reporting endpoints (same literal as the scheme).</summary>
    public const string PolicyName = SchemeName;

    protected override string TokenScheme => ReportingTokenService.TokenScheme;
    protected override string TokenKind => "reporting token";
    protected override string RecordItemKey => ReportingTokenContextKeys.TokenRecord;
    protected override string TokenIdClaim => ReportingTokenContextKeys.TokenIdClaim;
    protected override string TenantIdClaim => ReportingTokenContextKeys.TenantIdClaim;
    protected override string ScopesClaim => ReportingTokenContextKeys.ScopesClaim;
    protected override string TokenPrefixClaim => ReportingTokenContextKeys.TokenPrefixClaim;

    // The reporting API is a versioned contract: external BI tools depend on these
    // {error, message} bodies, so they stay as they are rather than the problem shape.
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
}

public static class ReportingTokenContextKeys
{
    public const string TokenRecord = "ReportingTokenRecord";
    public const string TokenIdClaim = "reporting_token_id";
    public const string TenantIdClaim = "reporting_tenant_id";
    public const string ScopesClaim = "reporting_scopes";
    public const string TokenPrefixClaim = "reporting_token_prefix";
}

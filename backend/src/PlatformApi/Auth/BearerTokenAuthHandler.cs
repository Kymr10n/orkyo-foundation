using System.Security.Claims;
using System.Text.Encodings.Web;
using Api.Services.PlatformApi;
using Api.Services.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.PlatformApi.Auth;

/// <summary>
/// The shared flow of every <c>{scheme}_{prefix}_{secret}</c> bearer-token scheme: read the
/// header, ignore tokens of another scheme, validate, stash the record, touch last_used_at, and
/// issue the claims. Each derived handler names its own scheme, claim types and item key, and
/// keeps its own challenge and forbidden responses.
/// </summary>
public abstract class BearerTokenAuthHandler<TRecord, TService>(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TService tokenService,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    where TRecord : TokenRecordBase
    where TService : class, ITokenVerifier<TRecord>
{
    /// <summary>The token scheme this handler accepts, without the trailing underscore.</summary>
    protected abstract string TokenScheme { get; }

    /// <summary>Human name for logs and the failure reason, e.g. "reporting token".</summary>
    protected abstract string TokenKind { get; }

    /// <summary>Key under which the validated record goes into <c>HttpContext.Items</c>.</summary>
    protected abstract string RecordItemKey { get; }

    protected abstract string TokenIdClaim { get; }
    protected abstract string TenantIdClaim { get; }
    protected abstract string ScopesClaim { get; }
    protected abstract string TokenPrefixClaim { get; }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = authorization["Bearer ".Length..].Trim();
        if (!token.StartsWith(TokenScheme + "_", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var record = await tokenService.ValidateAsync(token, Context.RequestAborted);
        if (record is null)
        {
            Logger.LogWarning("{TokenKind} validation failed for prefix {Prefix}",
                TokenKind, TokenCredentialHelper.ExtractPrefix(token, TokenScheme));
            return AuthenticateResult.Fail($"Invalid, expired, or revoked {TokenKind}.");
        }

        // Read downstream by filters, audit and context-enrichment middleware.
        Context.Items[RecordItemKey] = record;

        // Touch last_used_at asynchronously — don't block the request. Through a fresh DI
        // scope, NOT the request's token service: this task outlives the request, whose scope
        // (and its DB connection factory) is disposed the moment the response completes. Using
        // the scoped instance made the update race disposal and silently stop under load — and
        // last_used_at is the field an admin reads to spot a stale or stolen token.
        _ = TouchLastUsedInOwnScopeAsync(record.Id);

        var claims = new[]
        {
            new Claim(TokenIdClaim, record.Id.ToString()),
            new Claim(TenantIdClaim, record.TenantId.ToString()),
            new Claim(ScopesClaim, record.Scopes),
            new Claim(TokenPrefixClaim, record.TokenPrefix),
        };

        // Identity.Name resolves to the token id for every token kind: the rate limiter partitions
        // on UserOrIpKey, which would otherwise put every token behind one NAT'd egress IP into a
        // single bucket (reporting tokens did, until this moved here from the API-token handler).
        var identity = new ClaimsIdentity(claims, Scheme.Name, TokenIdClaim, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>Background update with its own scope, so it cannot race request disposal.</summary>
    private async Task TouchLastUsedInOwnScopeAsync(Guid tokenId)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider
                .GetRequiredService<TService>()
                .TouchLastUsedAsync(tokenId);
        }
        catch (Exception ex)
        {
            // Nothing awaits this task; an escaped exception would only surface as an
            // unobserved-task event. The touch is best-effort by design.
            Logger.LogWarning(ex, "Background last_used_at update failed for {TokenKind} {TokenId}", TokenKind, tokenId);
        }
    }
}

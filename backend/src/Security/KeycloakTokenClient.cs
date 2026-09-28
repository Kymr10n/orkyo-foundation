using System.Text.Json;
using Api.Endpoints;
using Orkyo.Shared.Keycloak;

namespace Api.Security;

/// <summary>
/// The BFF's two calls to Keycloak's token endpoint — the login code exchange and the session
/// refresh — with one response parser. They used to be two copies that had drifted: login checked
/// every field, refresh read them with <c>GetProperty</c> and threw on a missing one.
/// </summary>
public sealed class KeycloakTokenClient(
    IHttpClientFactory httpClientFactory,
    KeycloakOptions keycloakOptions,
    ILogger<KeycloakTokenClient> logger)
{
    /// <summary>The named <see cref="HttpClient"/> for the token endpoint (products reuse it).</summary>
    public const string HttpClientName = "BffKeycloak";

    /// <summary>Keycloak's lifetime default, used when a response omits <c>expires_in</c>.</summary>
    private const int DefaultExpiresInSeconds = 300;

    /// <summary>Exchanges an authorization code (PKCE) for tokens; null on any failure.</summary>
    public async Task<BffAuthEndpoints.TokenResponse?> ExchangeCodeAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct = default)
    {
        using var response = await PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = keycloakOptions.BackendClientId,
            ["client_secret"] = keycloakOptions.BackendClientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
        }, ct);
        return await ParseAsync(response, logger, requireIdToken: true, ct);
    }

    /// <summary>
    /// Refreshes a session's tokens with the client that issued them. <c>Rejected</c> is true only
    /// for <c>invalid_grant</c> — the refresh token is dead for good — as opposed to a failure a
    /// retry may cure. Throws only what the transport throws.
    /// </summary>
    public async Task<(BffAuthEndpoints.TokenResponse? Tokens, bool Rejected)> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
    {
        using var response = await PostAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["refresh_token"] = refreshToken,
        }, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Keycloak token refresh returned {StatusCode}", response.StatusCode);
            return (null, await IsInvalidGrantAsync(response, ct));
        }
        // The id token is not needed after login; a refresh answer without one is still usable.
        return (await ParseAsync(response, logger, requireIdToken: false, ct), false);
    }

    private async Task<HttpResponseMessage> PostAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        return await client.PostAsync(
            $"{keycloakOptions.InternalAuthority}/protocol/openid-connect/token", new FormUrlEncodedContent(form), ct);
    }

    /// <summary>
    /// Reads a token endpoint answer: null on a non-success status, a body that is not JSON, or a
    /// missing token. The error body is logged only on failure and never holds the request's
    /// credentials.
    /// </summary>
    internal static async Task<BffAuthEndpoints.TokenResponse?> ParseAsync(
        HttpResponseMessage response, ILogger logger, bool requireIdToken, CancellationToken ct = default)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Keycloak token request failed: {StatusCode} {Body}", response.StatusCode, errorBody);
            return null;
        }

        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var tokens = Parse(doc.RootElement, requireIdToken);
            if (tokens is null)
                logger.LogError("Keycloak token response missing required fields (access_token / refresh_token{IdToken})",
                    requireIdToken ? " / id_token" : "");
            return tokens;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Keycloak token response is not JSON");
            return null;
        }
    }

    /// <summary>The pure half of <see cref="ParseAsync"/>: the fields of a token endpoint answer.</summary>
    internal static BffAuthEndpoints.TokenResponse? Parse(JsonElement root, bool requireIdToken)
    {
        if (root.ValueKind != JsonValueKind.Object
            || StringOf(root, "access_token") is not { } accessToken
            || StringOf(root, "refresh_token") is not { } refreshToken)
            return null;

        var idToken = StringOf(root, "id_token");
        if (requireIdToken && idToken is null) return null;

        var expiresIn = root.TryGetProperty("expires_in", out var exp) && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt32(out var seconds)
            ? seconds
            : DefaultExpiresInSeconds;

        return new BffAuthEndpoints.TokenResponse(accessToken, refreshToken, idToken ?? "", expiresIn);
    }

    private static string? StringOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// True for the OAuth 2.0 <c>invalid_grant</c> error (RFC 6749 §5.2), which Keycloak returns
    /// for an expired, revoked or logged-out refresh token and for a disabled user. Any other
    /// failure — a client-credential error included — is not the session's fault.
    /// </summary>
    private static async Task<bool> IsInvalidGrantAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.BadRequest)
            return false;
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && StringOf(doc.RootElement, "error") == "invalid_grant";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

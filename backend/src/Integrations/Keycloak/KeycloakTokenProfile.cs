using System.Security.Claims;
using System.Text.Json;
using Api.Security;

namespace Api.Integrations.Keycloak;

/// <summary>
/// Maps Keycloak JWT claims to internal identity types.
/// This is the ONLY place where Keycloak claim parsing should occur.
///
/// Lives in <c>orkyo-foundation</c> because the Keycloak JWT claim contract is
/// identical across multi-tenant SaaS and single-tenant Community deployments;
/// composition layers consume the shared profile rather than duplicating
/// claim-parsing logic.
/// </summary>
public sealed class KeycloakTokenProfile
{
    private readonly ClaimsPrincipal _principal;
    private readonly Lazy<IReadOnlyList<string>> _realmRoles;

    private KeycloakTokenProfile(ClaimsPrincipal principal)
    {
        _principal = principal;
        _realmRoles = new Lazy<IReadOnlyList<string>>(ParseRealmRoles);
    }

    /// <summary>Subject identifier (Keycloak user ID)</summary>
    public string? Subject => GetClaim(KeycloakClaims.Subject);

    /// <summary>Email address</summary>
    public string? Email => GetClaim(KeycloakClaims.Email);

    /// <summary>Whether email is verified</summary>
    public bool EmailVerified => GetClaimBool(KeycloakClaims.EmailVerified);

    /// <summary>Preferred username</summary>
    public string? PreferredUsername => GetClaim(KeycloakClaims.PreferredUsername);

    /// <summary>Full name</summary>
    public string? Name => GetClaim(KeycloakClaims.Name);

    /// <summary>Given name</summary>
    public string? GivenName => GetClaim(KeycloakClaims.GivenName);

    /// <summary>Family name</summary>
    public string? FamilyName => GetClaim(KeycloakClaims.FamilyName);

    /// <summary>Token issuer</summary>
    public string? Issuer => GetClaim(KeycloakClaims.Issuer);

    /// <summary>Token audience</summary>
    public string? Audience => GetClaim(KeycloakClaims.Audience);

    /// <summary>Authorized party (client ID)</summary>
    public string? AuthorizedParty => GetClaim(KeycloakClaims.AuthorizedParty);

    /// <summary>Session ID</summary>
    public string? SessionId => GetClaim(KeycloakClaims.SessionId);

    /// <summary>Best effort display name (tries multiple claim sources)</summary>
    public string? DisplayName =>
        Name ??
        (GivenName != null && FamilyName != null ? $"{GivenName} {FamilyName}" : null) ??
        PreferredUsername ??
        Email?.Split('@').FirstOrDefault();

    /// <summary>Whether this appears to be a valid Keycloak token</summary>
    public bool IsValid => !string.IsNullOrEmpty(Subject);

    /// <summary>Whether the principal is authenticated</summary>
    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated ?? false;

    /// <summary>
    /// Create a token profile from a ClaimsPrincipal.
    /// </summary>
    public static KeycloakTokenProfile FromPrincipal(ClaimsPrincipal principal)
    {
        return new KeycloakTokenProfile(principal);
    }

    /// <summary>
    /// Convert to internal ExternalIdentityToken format.
    /// </summary>
    public ExternalIdentityToken? ToExternalIdentityToken()
    {
        if (!IsValid || string.IsNullOrEmpty(Subject))
            return null;

        return new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = Subject,
            Email = Email,
            EmailVerified = EmailVerified,
            DisplayName = DisplayName,
            Issuer = Issuer,
            Audience = Audience
        };
    }

    private string? GetClaim(string claimType)
    {
        return _principal.FindFirst(claimType)?.Value;
    }

    private bool GetClaimBool(string claimType)
    {
        var value = GetClaim(claimType);
        return bool.TryParse(value, out var result) && result;
    }

    /// <summary>
    /// Realm-level roles assigned to this user.
    /// Parsed from the realm_access claim.
    /// </summary>
    public IReadOnlyList<string> RealmRoles => _realmRoles.Value;

    /// <summary>
    /// Whether this user has the site-admin role (global admin across all tenants).
    /// </summary>
    public bool IsSiteAdmin => RealmRoles.Contains(KeycloakClaims.SiteAdminRole);

    /// <summary>
    /// Check if the user has a specific realm role.
    /// </summary>
    public bool HasRealmRole(string role) => RealmRoles.Contains(role);

    private IReadOnlyList<string> ParseRealmRoles()
    {
        var realmAccess = GetClaim(KeycloakClaims.RealmAccess);
        if (string.IsNullOrEmpty(realmAccess))
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var rolesElement) &&
                rolesElement.ValueKind == JsonValueKind.Array)
            {
                return rolesElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Invalid JSON in claim - return empty
        }

        return Array.Empty<string>();
    }
}

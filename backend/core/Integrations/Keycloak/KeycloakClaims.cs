namespace Api.Integrations.Keycloak;

/// <summary>
/// Keycloak JWT claim type constants and well-known realm role names.
/// This is the ONLY place in the platform where Keycloak claim names should
/// be defined. All claim extraction must go through <c>KeycloakTokenProfile</c>.
///
/// Lives in <c>orkyo-foundation</c> because Keycloak is the auth substrate
/// in both multi-tenant SaaS and single-tenant Community deployments, and
/// the claim contract is identical across both products.
/// </summary>
public static class KeycloakClaims
{
    /// <summary>Subject identifier (unique user ID in Keycloak)</summary>
    public const string Subject = "sub";

    /// <summary>Email address</summary>
    public const string Email = "email";

    /// <summary>Whether email is verified</summary>
    public const string EmailVerified = "email_verified";

    /// <summary>Preferred username</summary>
    public const string PreferredUsername = "preferred_username";

    /// <summary>Given name (first name)</summary>
    public const string GivenName = "given_name";

    /// <summary>Family name (last name)</summary>
    public const string FamilyName = "family_name";

    /// <summary>Full name</summary>
    public const string Name = "name";

    /// <summary>Token issuer</summary>
    public const string Issuer = "iss";

    /// <summary>Token audience</summary>
    public const string Audience = "aud";

    /// <summary>Authorized party (client that requested the token)</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>Session ID</summary>
    public const string SessionId = "sid";

    /// <summary>Session state</summary>
    public const string SessionState = "session_state";

    /// <summary>Token type</summary>
    public const string TokenType = "typ";

    /// <summary>Scope</summary>
    public const string Scope = "scope";

    /// <summary>Issued at timestamp</summary>
    public const string IssuedAt = "iat";

    /// <summary>Expiration timestamp</summary>
    public const string Expiration = "exp";

    /// <summary>Authentication time</summary>
    public const string AuthTime = "auth_time";

    /// <summary>Realm access (contains realm roles)</summary>
    public const string RealmAccess = "realm_access";

    // Well-known realm role names

    /// <summary>Site administrator role</summary>
    public const string SiteAdminRole = "site-admin";
}

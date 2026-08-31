namespace Api.Security;

/// <summary>
/// Represents the authentication provider type.
/// Allows the system to support multiple identity providers.
/// </summary>
public enum AuthProvider
{
    /// <summary>Local email/password authentication (legacy)</summary>
    Local,

    /// <summary>Keycloak OIDC authentication</summary>
    Keycloak,

    /// <summary>Azure AD authentication (future)</summary>
    AzureAD,

    /// <summary>Google OAuth (future)</summary>
    Google,

    /// <summary>
    /// A per-tenant API access token acting on its own behalf (MCP server, automated integrations).
    /// There is no human behind it: the user id is derived from the token, and its tenant role
    /// comes from the token's scopes rather than a membership row.
    /// </summary>
    ApiToken
}

/// <summary>
/// Represents the authenticated user's identity context.
/// This is the internal representation - no vendor-specific types allowed.
/// </summary>
public sealed class PrincipalContext
{
    /// <summary>Internal user ID (from control_plane.users)</summary>
    public required Guid UserId { get; init; }

    /// <summary>User's email address</summary>
    public required string Email { get; init; }

    /// <summary>User's display name</summary>
    public string? DisplayName { get; init; }

    /// <summary>Authentication provider used</summary>
    public required AuthProvider AuthProvider { get; init; }

    /// <summary>External subject identifier (e.g., Keycloak sub)</summary>
    public string? ExternalSubject { get; init; }

    /// <summary>
    /// The identity provider's session id (Keycloak's <c>sid</c>), when the request carries
    /// one. It tells two visitors apart where the account cannot: the public demo shares a
    /// single user, so this is the only per-visitor handle that exists.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Whether this user has the site-admin role (global admin across all tenants)</summary>
    public bool IsSiteAdmin { get; init; }

    /// <summary>Whether this is an anonymous/unauthenticated context</summary>
    public bool IsAuthenticated => UserId != Guid.Empty;

    /// <summary>Creates an anonymous (unauthenticated) principal</summary>
    public static PrincipalContext Anonymous => new()
    {
        UserId = Guid.Empty,
        Email = string.Empty,
        AuthProvider = AuthProvider.Local,
        IsSiteAdmin = false
    };
}

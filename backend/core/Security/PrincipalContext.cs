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
    Google
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

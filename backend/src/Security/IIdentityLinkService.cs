using Api.Helpers;

namespace Api.Security;

/// <summary>
/// Result of an identity link operation.
/// </summary>
public sealed class IdentityLinkResult
{
    /// <summary>Whether the operation was successful</summary>
    public required bool Success { get; init; }

    /// <summary>The linked user's internal ID</summary>
    public Guid? UserId { get; init; }

    /// <summary>The linked user's email</summary>
    public string? Email { get; init; }

    /// <summary>The linked user's display name</summary>
    public string? DisplayName { get; init; }

    /// <summary>Whether this was a new user creation</summary>
    public bool IsNewUser { get; init; }

    /// <summary>Error message if not successful</summary>
    public string? Error { get; init; }

    /// <summary>Machine-readable error code for API responses</summary>
    public string? ErrorCode { get; init; }

    public static IdentityLinkResult Linked(Guid userId, string email, string? displayName, bool isNew = false) => new()
    {
        Success = true,
        UserId = userId,
        Email = email,
        DisplayName = displayName,
        IsNewUser = isNew
    };

    public static IdentityLinkResult Failed(string error, string? errorCode = null) => new()
    {
        Success = false,
        Error = error,
        ErrorCode = errorCode ?? ProblemDetailsHelper.AuthCodes.IdentityNotLinked
    };
}

/// <summary>
/// External identity token profile (provider-agnostic).
/// All provider-specific token parsing happens before creating this.
/// </summary>
public sealed class ExternalIdentityToken
{
    /// <summary>The authentication provider</summary>
    public required AuthProvider Provider { get; init; }

    /// <summary>External subject identifier (e.g., Keycloak sub)</summary>
    public required string Subject { get; init; }

    /// <summary>Email from the token (may be null for some providers)</summary>
    public string? Email { get; init; }

    /// <summary>Whether the email is verified by the provider</summary>
    public bool EmailVerified { get; init; }

    /// <summary>Display name / preferred username</summary>
    public string? DisplayName { get; init; }

    /// <summary>Token issuer</summary>
    public string? Issuer { get; init; }

    /// <summary>Token audience</summary>
    public string? Audience { get; init; }
}

/// <summary>
/// Service for linking external identities to internal users.
/// Handles the bootstrap flow where external users are linked/created.
/// </summary>
public interface IIdentityLinkService
{
    /// <summary>
    /// Find an existing user by their external identity.
    /// </summary>
    /// <param name="provider">The authentication provider</param>
    /// <param name="externalSubject">The external subject identifier</param>
    /// <returns>The principal context if found, null otherwise</returns>
    Task<PrincipalContext?> FindByExternalIdentityAsync(AuthProvider provider, string externalSubject);

    /// <summary>
    /// Link an external identity to an internal user, creating the user if necessary.
    /// This is idempotent - calling it multiple times with the same identity is safe.
    /// </summary>
    /// <param name="token">The external identity token</param>
    /// <returns>The result of the link operation</returns>
    Task<IdentityLinkResult> LinkIdentityAsync(ExternalIdentityToken token);

    /// <summary>
    /// Get the user's memberships across all tenants.
    /// </summary>
    /// <param name="userId">The internal user ID</param>
    /// <returns>List of tenant memberships</returns>
    Task<IReadOnlyList<TenantMembership>> GetUserMembershipsAsync(Guid userId);

    /// <summary>
    /// Get the user's role in a specific tenant.
    /// </summary>
    /// <param name="userId">The internal user ID</param>
    /// <param name="tenantId">The tenant ID</param>
    /// <returns>The user's role, or None if not a member</returns>
    Task<TenantRole> GetUserTenantRoleAsync(Guid userId, Guid tenantId);
}

/// <summary>
/// Tenant membership information.
/// </summary>
public sealed class TenantMembership
{
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string TenantName { get; init; }
    public required TenantRole Role { get; init; }
    public required string Status { get; init; }
}

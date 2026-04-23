namespace Api.Services;

/// <summary>
/// SQL contract for the Keycloak self-registration provisioning flow.
///
/// Three queries:
/// <list type="bullet">
///   <item><description>Case-insensitive lookup of a user by email
///   (returns id/email/display_name/status; used for invitation matching).</description></item>
///   <item><description>INSERT a brand-new active user row with explicit id and timestamps
///   (used inside a transaction with an identity link).</description></item>
///   <item><description>INSERT an identity link row with explicit id (transactional variant
///   without ON CONFLICT, used during initial user creation when the link cannot
///   already exist).</description></item>
/// </list>
///
/// Provisioning a user from a verified external identity is structurally identical
/// in multi-tenant SaaS and single-tenant Community deployments, so the SQL belongs
/// in foundation by default. The Keycloak provider literal is shared with
/// <see cref="UserIdentityLinkQueryContract.KeycloakProviderLiteral"/>.
/// </summary>
public static class KeycloakUserProvisioningQueryContract
{
    public const string EmailParameterName = "email";
    public const string IdParameterName = "id";
    public const string UserIdParameterName = "userId";
    public const string DisplayNameParameterName = "displayName";
    public const string SubjectParameterName = "subject";

    /// <summary>Status literal stored in <c>users.status</c> for a fully active account.</summary>
    public const string ActiveUserStatus = "active";

    public static string BuildSelectUserByLowerEmailSql()
    {
        return @"
            SELECT id, email, display_name, status
            FROM users 
            WHERE LOWER(email) = LOWER(@email)";
    }

    public static string BuildInsertNewActiveUserSql()
    {
        return @"
            INSERT INTO users (id, email, display_name, status, last_login_at, created_at, updated_at)
            VALUES (@id, @email, @displayName, 'active', NOW(), NOW(), NOW())";
    }

    public static string BuildInsertIdentityLinkWithIdSql()
    {
        return @"
            INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@id, @userId, 'keycloak', @subject, @email, NOW())";
    }
}

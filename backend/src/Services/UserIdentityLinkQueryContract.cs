namespace Api.Services;

/// <summary>
/// SQL contract for the platform <c>user_identities</c> + <c>users.last_login_at</c>
/// surfaces backing the Keycloak (and any future external) identity link flow.
///
/// Three queries:
/// <list type="bullet">
///   <item><description>SELECT a principal projection by external provider + subject,
///   filtered to active users (today: only the <c>keycloak</c> provider literal).</description></item>
///   <item><description>INSERT a (provider, provider_subject) link row with
///   <c>ON CONFLICT DO NOTHING</c> for idempotency.</description></item>
///   <item><description>UPDATE <c>users.last_login_at</c> + <c>users.updated_at</c>
///   to mark a successful sign-in.</description></item>
/// </list>
///
/// All three are structurally identical in multi-tenant SaaS and single-tenant
/// Community deployments (one Keycloak realm per product, one shared user table).
/// </summary>
public static class UserIdentityLinkQueryContract
{
    public const string SubjectParameterName = "subject";
    public const string UserIdParameterName = "userId";
    public const string EmailParameterName = "email";
    public const string LastLoginUserIdParameterName = "id";

    /// <summary>Provider literal stored in <c>user_identities.provider</c> for Keycloak.</summary>
    public const string KeycloakProviderLiteral = "keycloak";

    public static string BuildSelectActiveUserByExternalIdentitySql()
    {
        return @"
            SELECT u.id, u.email, u.display_name
            FROM users u
            INNER JOIN user_identities ui ON u.id = ui.user_id
            WHERE ui.provider = 'keycloak' 
              AND ui.provider_subject = @subject
              AND u.status = 'active'";
    }

    public static string BuildInsertIdentityLinkSql()
    {
        return @"
            INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)
            VALUES (@userId, 'keycloak', @subject, @email, NOW())
            ON CONFLICT (provider, provider_subject) DO NOTHING";
    }

    public static string BuildUpdateLastLoginSql()
    {
        return @"
            UPDATE users
            SET last_login_at = NOW(), updated_at = NOW()
            WHERE id = @id";
    }
}

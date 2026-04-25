namespace Api.Services;

/// <summary>
/// SQL contract for the user account-lifecycle confirm-activity flow.
///
/// Two queries:
/// <list type="bullet">
///   <item><description>Lookup of an active lifecycle record by confirm token (returns the
///   minimal projection needed to drive Keycloak re-enable + clear).</description></item>
///   <item><description>Clear of all lifecycle columns on the matched user row.</description></item>
/// </list>
///
/// Both operate on the control-plane <c>users</c> table and are structurally
/// identical in multi-tenant SaaS and single-tenant Community deployments.
/// </summary>
public static class AccountLifecycleQueryContract
{
    public const string ConfirmTokenParameterName = "token";
    public const string UserIdParameterName = "id";

    /// <summary>
    /// Status string stored in <c>users.lifecycle_status</c> indicating the user
    /// has been disabled in Keycloak and must be re-enabled on confirm.
    /// </summary>
    public const string DormantLifecycleStatus = "dormant";

    public static string BuildSelectUserByConfirmTokenSql()
    {
        return @"
            SELECT id, keycloak_id, display_name, lifecycle_status
            FROM users
            WHERE lifecycle_confirm_token = @token
              AND lifecycle_status IS NOT NULL
        ";
    }

    public static string BuildClearLifecycleStateSql()
    {
        return @"
            UPDATE users
            SET lifecycle_status = NULL,
                lifecycle_warning_count = 0,
                lifecycle_last_warned_at = NULL,
                lifecycle_dormant_since = NULL,
                lifecycle_confirm_token = NULL,
                updated_at = NOW()
            WHERE id = @id
        ";
    }
}

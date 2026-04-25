namespace Api.Services;

/// <summary>
/// SQL contract for control-plane <c>users</c> existence checks by id.
/// Structurally identical in multi-tenant SaaS and single-tenant Community.
/// </summary>
public static class UserLookupByIdQueryContract
{
    public const string UserIdParameterName = "userId";

    /// <summary>
    /// Returns a single boolean — <c>true</c> if a user row with the given id exists.
    /// Uses <c>EXISTS(\u2026)</c> so the planner can short-circuit on the PK index.
    /// </summary>
    public static string BuildExistsByIdSql()
    {
        return "SELECT EXISTS(SELECT 1 FROM users WHERE id = @userId)";
    }
}

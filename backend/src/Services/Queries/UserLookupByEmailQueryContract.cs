namespace Api.Services;

/// <summary>
/// SQL contract for looking up a control-plane user's id by email.
/// Used during initial-admin provisioning where the user has already
/// authenticated via Keycloak and exists in the <c>users</c> table.
/// </summary>
public static class UserLookupByEmailQueryContract
{
    public const string EmailParameterName = "email";

    public static string BuildSelectUserIdByEmailSql()
    {
        return @"
            SELECT id FROM users WHERE email = @email
        ";
    }
}

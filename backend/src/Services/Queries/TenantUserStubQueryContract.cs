namespace Api.Services;

/// <summary>
/// SQL contract for inserting a minimal user stub into a tenant database.
///
/// The full user record lives in <c>control_plane.users</c>; tenant databases
/// only need a stub row so foreign-key references from tenant-scoped tables
/// (e.g. spaces, requests, audit_events) remain valid. Email is normalized to
/// lowercase by the caller before binding.
/// </summary>
public static class TenantUserStubQueryContract
{
    public const string IdParameterName = "id";
    public const string EmailParameterName = "email";

    public static string BuildInsertUserStubSql()
    {
        return @"
            INSERT INTO users (id, email, created_at)
            VALUES (@id, @email, NOW())
            ON CONFLICT DO NOTHING
        ";
    }
}

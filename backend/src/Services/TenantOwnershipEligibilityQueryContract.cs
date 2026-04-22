namespace Api.Services;

public static class TenantOwnershipEligibilityQueryContract
{
    public const string UserIdParameterName = "userId";

    public static string BuildActiveOwnedTenantCountSql()
    {
        return @"
            SELECT COUNT(*) FROM tenants
            WHERE owner_user_id = @userId AND status != 'deleting'
        ";
    }
}

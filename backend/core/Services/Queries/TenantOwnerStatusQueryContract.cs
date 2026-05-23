namespace Api.Services;

public static class TenantOwnerStatusQueryContract
{
    public const string TenantIdParameterName = "tenantId";

    public static string BuildSelectByTenantIdSql()
    {
        return @"
            SELECT owner_user_id, status FROM tenants WHERE id = @tenantId
        ";
    }
}

namespace Api.Services;

public static class TenantMutationQueryContract
{
    public const string TenantIdParameterName = "tenantId";
    public const string UserIdParameterName = "userId";
    public const string DisplayNameParameterName = "displayName";
    public const string NewOwnerIdParameterName = "newOwnerId";

    public static string BuildMarkDeletingSql()
    {
        return @"
            UPDATE tenants SET status = 'deleting', updated_at = NOW() WHERE id = @tenantId
        ";
    }

    public static string BuildMarkActiveSql()
    {
        return @"
            UPDATE tenants SET status = 'active', updated_at = NOW() WHERE id = @tenantId
        ";
    }

    public static string BuildTouchLastActivitySql()
    {
        return @"
            UPDATE tenants SET last_activity_at = NOW() WHERE id = @tenantId
        ";
    }

    public static string BuildUpdateDisplayNameSql()
    {
        return @"
            UPDATE tenants SET display_name = @displayName, updated_at = NOW()
            WHERE id = @tenantId
        ";
    }

    public static string BuildTransferOwnershipSql()
    {
        return @"
            UPDATE tenants SET owner_user_id = @newOwnerId, updated_at = NOW()
            WHERE id = @tenantId
        ";
    }

    public static string BuildDeleteMembershipSql()
    {
        return @"
            DELETE FROM tenant_memberships
            WHERE tenant_id = @tenantId AND user_id = @userId
        ";
    }

    public static string BuildReactivateSuspendedTenantSql()
    {
        return @"
            UPDATE tenants
            SET status = 'active',
                suspended_at = NULL,
                suspension_reason = NULL,
                last_activity_at = NOW()
            WHERE id = @tenantId AND status = 'suspended'
        ";
    }
}

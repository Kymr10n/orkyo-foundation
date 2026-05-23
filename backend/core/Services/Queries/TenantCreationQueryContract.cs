namespace Api.Services;

public static class TenantCreationQueryContract
{
    public const string SlugParameterName = "slug";
    public const string DisplayNameParameterName = "displayName";
    public const string DbIdentifierParameterName = "dbIdentifier";
    public const string OwnerIdParameterName = "ownerId";
    public const string UserIdParameterName = "userId";
    public const string TenantIdParameterName = "tenantId";

    public static string BuildInsertTenantSql()
    {
        return $@"
            INSERT INTO tenants (slug, display_name, status, db_identifier, owner_user_id, created_at, updated_at)
            VALUES (@slug, @displayName, 'active', @dbIdentifier, @ownerId, NOW(), NOW())
            RETURNING {TenantRecordQueryContract.Projection}
        ";
    }

    public static string BuildInsertOwnerMembershipSql()
    {
        return @"
            INSERT INTO tenant_memberships (user_id, tenant_id, role, status, created_at, updated_at)
            VALUES (@userId, @tenantId, 'admin', 'active', NOW(), NOW())
        ";
    }

    public static string BuildSelectUserEmailByIdSql()
    {
        return @"
            SELECT email FROM users WHERE id = @userId
        ";
    }
}

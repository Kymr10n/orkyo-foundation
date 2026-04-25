namespace Api.Services;

public static class TenantRecordQueryContract
{
    public const string TenantIdParameterName = "id";
    public const string TenantSlugParameterName = "slug";

    public const int IdOrdinal = 0;
    public const int SlugOrdinal = 1;
    public const int DisplayNameOrdinal = 2;
    public const int StatusOrdinal = 3;
    public const int DbIdentifierOrdinal = 4;
    public const int OwnerUserIdOrdinal = 5;
    public const int CreatedAtOrdinal = 6;

    public static string Projection => "id, slug, display_name, status, db_identifier, owner_user_id, created_at";

    public static string BuildSelectByIdSql()
    {
        return $@"
            SELECT {Projection}
            FROM tenants WHERE id = @id
        ";
    }

    public static string BuildSelectBySlugSql()
    {
        return $@"
            SELECT {Projection}
            FROM tenants WHERE slug = @slug
        ";
    }
}

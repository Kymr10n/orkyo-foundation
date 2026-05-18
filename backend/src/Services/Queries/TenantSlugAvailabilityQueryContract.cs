namespace Api.Services;

public static class TenantSlugAvailabilityQueryContract
{
    public const string SlugParameterName = "slug";

    public static string BuildSelectExistingTenantIdBySlugSql()
    {
        return @"
            SELECT id FROM tenants WHERE slug = @slug
        ";
    }
}

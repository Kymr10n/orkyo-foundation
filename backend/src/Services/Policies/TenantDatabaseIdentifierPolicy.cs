namespace Api.Services;

public static class TenantDatabaseIdentifierPolicy
{
    public static string BuildFromSlug(string tenantSlug)
    {
        return $"tenant_{tenantSlug.Replace("-", "_")}";
    }
}

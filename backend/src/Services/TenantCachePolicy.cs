namespace Api.Services;

public static class TenantCachePolicy
{
    public static bool IsFresh(DateTime expiresAtUtc, DateTime nowUtc)
    {
        return expiresAtUtc > nowUtc;
    }
}
namespace Api.Services;

public static class TenantCacheKeyPolicy
{
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    public static string Canonicalize(string slug)
    {
        return slug;
    }
}
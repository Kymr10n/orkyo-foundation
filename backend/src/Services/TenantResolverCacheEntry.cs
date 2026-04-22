namespace Api.Services;

public sealed class TenantResolverCacheEntry
{
    public TenantResolverCacheEntry(TenantContext? context, DateTime expiresAtUtc)
    {
        Context = context;
        ExpiresAtUtc = expiresAtUtc;
    }

    public TenantContext? Context { get; }

    public DateTime ExpiresAtUtc { get; }

    public bool IsFresh(DateTime nowUtc)
    {
        return TenantCachePolicy.IsFresh(ExpiresAtUtc, nowUtc);
    }
}
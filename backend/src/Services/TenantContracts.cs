using Api.Models;

namespace Api.Services;

public class TenantContext
{
    public required Guid TenantId { get; init; }
    public required string TenantSlug { get; init; }
    public required string TenantDbConnectionString { get; init; }
    public required ServiceTier Tier { get; init; }
    public required string Status { get; init; }
    public string? SuspensionReason { get; init; }

    public bool IsSuspended => TenantStatusPolicy.IsSuspended(Status);
    public bool IsActive => TenantStatusPolicy.IsActive(Status);
}

public interface ITenantResolver
{
    Task<TenantContext?> ResolveTenantAsync(string? subdomain, string? tenantHeader);

    /// <summary>Evict a cached tenant entry (e.g. after status change).</summary>
    void InvalidateCache(string slug);
}
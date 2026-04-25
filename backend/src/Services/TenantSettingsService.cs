using System.Collections.Concurrent;
using Api.Models;
using Api.Repositories;
using Orkyo.Shared;

namespace Api.Services;

public class TenantSettingsService : ITenantSettingsService
{
    private readonly ITenantSettingsRepository _tenantRepo;
    private readonly ISiteSettingsRepository _siteRepo;
    private readonly OrgContext _orgContext;
    private readonly ILogger<TenantSettingsService> _logger;

    // Per-tenant in-memory cache, keyed by TenantId.
    // Invalidated on write. Expires on next request after TTL.
    private static readonly ConcurrentDictionary<Guid, (TenantSettings Settings, DateTime ExpiresAt)> _cache = new();
    // Separate cache for site-level settings (shared across all tenants).
    private static readonly object _siteCacheLock = new();
    private static (Dictionary<string, string> Overrides, DateTime ExpiresAt)? _siteCache;
    private static readonly TimeSpan CacheTtl = TimePolicyConstants.CacheTtl;

    /// <summary>Clear all cached settings (used by integration tests after direct DB cleanup).</summary>
    public static void ClearCache()
    {
        _cache.Clear();
        lock (_siteCacheLock) { _siteCache = null; }
    }

    public TenantSettingsService(
        ITenantSettingsRepository tenantRepo,
        ISiteSettingsRepository siteRepo,
        OrgContext orgContext,
        ILogger<TenantSettingsService> logger)
    {
        _tenantRepo = tenantRepo;
        _siteRepo = siteRepo;
        _orgContext = orgContext;
        _logger = logger;
    }

    /// <summary>True when operating in site-admin context (no tenant selected).</summary>
    private bool IsSiteContext => _orgContext.OrgId == Guid.Empty;

    public async Task<TenantSettings> GetSettingsAsync()
    {
        try
        {
            if (IsSiteContext)
            {
                // Site-admin context: only load site-scoped overrides from control_plane
                var siteOverrides = await GetSiteOverridesAsync();
                return TenantSettingsOverrideApplier.Apply(siteOverrides);
            }

            var tenantId = _orgContext.OrgId;

            if (_cache.TryGetValue(tenantId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
            {
                return cached.Settings;
            }

            // Tenant context: only load tenant-scoped overrides from tenant DB
            var tenantOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                tenantOverrides = await _tenantRepo.GetAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tenant settings for {TenantId}, using defaults", tenantId);
            }

            var settings = TenantSettingsOverrideApplier.Apply(tenantOverrides);
            _cache[tenantId] = (settings, DateTime.UtcNow + CacheTtl);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings, using defaults");
            return TenantSettingsOverrideApplier.Defaults;
        }
    }

    public async Task<TenantSettings> UpdateSettingsAsync(Dictionary<string, string> updates)
    {
        foreach (var (key, value) in updates)
        {
            if (!TenantSettingDescriptorCatalog.ByKey.TryGetValue(key, out var descriptor))
            {
                throw new ArgumentException($"Unknown setting key: '{key}'");
            }

            TenantSettingsScopePolicy.EnsureWritableInScope(key, IsSiteContext, "modified");

            TenantSettingsValidator.Validate(descriptor, value);

            if (IsSiteContext)
            {
                await _siteRepo.UpsertAsync(key, value, descriptor.Category);
            }
            else
            {
                await _tenantRepo.UpsertAsync(key, value, descriptor.Category);
            }
        }

        // Invalidate the relevant cache
        if (IsSiteContext)
        {
            lock (_siteCacheLock) { _siteCache = null; }
            _logger.LogInformation("Updated {Count} site-level settings: {Keys}",
                updates.Count, string.Join(", ", updates.Keys));
        }
        else
        {
            _cache.TryRemove(_orgContext.OrgId, out _);
            _logger.LogInformation("Tenant {TenantId} updated {Count} tenant-level settings: {Keys}",
                _orgContext.OrgId, updates.Count, string.Join(", ", updates.Keys));
        }

        return await GetSettingsAsync();
    }

    public async Task<bool> ResetSettingAsync(string key)
    {
        TenantSettingsScopePolicy.EnsureWritableInScope(key, IsSiteContext, "reset");

        bool result;

        if (IsSiteContext)
        {
            result = await _siteRepo.DeleteAsync(key);
            if (result)
            {
                lock (_siteCacheLock) { _siteCache = null; }
                _logger.LogInformation("Reset site-level setting '{Key}' to default", key);
            }
        }
        else
        {
            result = await _tenantRepo.DeleteAsync(key);
            if (result)
            {
                _cache.TryRemove(_orgContext.OrgId, out _);
                _logger.LogInformation("Tenant {TenantId} reset setting '{Key}' to default",
                    _orgContext.OrgId, key);
            }
        }

        return result;
    }

    /// <summary>
    /// Get descriptors filtered by the current context's scope.
    /// Site context → site-scoped descriptors only.
    /// Tenant context → tenant-scoped descriptors only.
    /// </summary>
    public IReadOnlyList<TenantSettingDescriptor> GetDescriptors()
        => IsSiteContext ? TenantSettingDescriptorCatalog.SiteScope : TenantSettingDescriptorCatalog.TenantScope;

    /// <summary>Get all descriptors regardless of scope (for static metadata lookups).</summary>
    public static IReadOnlyList<TenantSettingDescriptor> GetAllDescriptors() => TenantSettingDescriptorCatalog.All;

    // ── Site cache helper ───────────────────────────────────────────

    private async Task<Dictionary<string, string>> GetSiteOverridesAsync()
    {
        lock (_siteCacheLock)
        {
            if (_siteCache.HasValue && _siteCache.Value.ExpiresAt > DateTime.UtcNow)
            {
                return new Dictionary<string, string>(_siteCache.Value.Overrides, StringComparer.OrdinalIgnoreCase);
            }
        }

        var overrides = await _siteRepo.GetAllAsync();

        lock (_siteCacheLock)
        {
            _siteCache = (overrides, DateTime.UtcNow + CacheTtl);
        }

        return overrides;
    }
}

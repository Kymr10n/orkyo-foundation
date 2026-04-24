using Api.Configuration;
using Api.Repositories;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Loads <see cref="RuntimeConfig"/> from the <c>site_settings</c> table.
/// Uses a TTL-based in-memory cache (same pattern as
/// <see cref="TenantSettingsService"/>).  Cache is invalidated on write.
/// </summary>
public interface ISiteSettingsService
{
    /// <summary>
    /// Get the current <see cref="RuntimeConfig"/>, with DB overrides applied
    /// on top of compiled defaults.  TTL-cached.
    /// </summary>
    Task<RuntimeConfig> GetRuntimeConfigAsync();

    /// <summary>
    /// Update one or more runtime settings.  Returns the new resolved config.
    /// Only keys present in <see cref="RuntimeConfig.KeyMap"/> are accepted.
    /// </summary>
    Task<RuntimeConfig> UpdateRuntimeConfigAsync(Dictionary<string, string> updates, Guid? actorUserId);

    /// <summary>
    /// Reset a runtime setting to its compiled default.
    /// </summary>
    Task<bool> ResetSettingAsync(string key);
}

public sealed class SiteSettingsService : ISiteSettingsService
{
    private readonly ISiteSettingsRepository _repo;
    private readonly ILogger<SiteSettingsService> _logger;

    // Static cache shared across all scoped instances — same as TenantSettingsService._siteCache.
    private static readonly object _cacheLock = new();
    private static (RuntimeConfig Config, DateTime ExpiresAt)? _cache;
    private static readonly TimeSpan CacheTtl = TimePolicyConstants.CacheTtl;

    /// <summary>Clear the cache (for integration tests after direct DB cleanup).</summary>
    internal static void ClearCache()
    {
        lock (_cacheLock) { _cache = null; }
    }

    public SiteSettingsService(
        ISiteSettingsRepository repo,
        ILogger<SiteSettingsService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<RuntimeConfig> GetRuntimeConfigAsync()
    {
        lock (_cacheLock)
        {
            if (_cache.HasValue && _cache.Value.ExpiresAt > DateTime.UtcNow)
                return _cache.Value.Config;
        }

        try
        {
            var overrides = await _repo.GetAllAsync();

            // Filter to only RuntimeConfig keys (site_settings may also contain
            // TenantSettings site-scoped overrides from the existing system).
            var runtimeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in overrides)
            {
                if (RuntimeConfig.KeyMap.ContainsKey(key))
                    runtimeOverrides[key] = value;
            }

            var config = RuntimeConfig.ApplyOverrides(runtimeOverrides);

            lock (_cacheLock)
            {
                _cache = (config, DateTime.UtcNow + CacheTtl);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load runtime config from DB, using compiled defaults");
            return RuntimeConfig.Defaults;
        }
    }

    public async Task<RuntimeConfig> UpdateRuntimeConfigAsync(Dictionary<string, string> updates, Guid? actorUserId)
    {
        foreach (var (key, value) in updates)
        {
            RuntimeConfig.ValidateValue(key, value);

            var category = RuntimeConfig.CategoryForKey(key);
            await _repo.UpsertAsync(key, value, category);
        }

        // Invalidate cache
        lock (_cacheLock) { _cache = null; }

        _logger.LogInformation("Updated {Count} runtime config setting(s): {Keys}",
            updates.Count, string.Join(", ", updates.Keys));

        return await GetRuntimeConfigAsync();
    }

    public async Task<bool> ResetSettingAsync(string key)
    {
        if (!RuntimeConfig.KeyMap.ContainsKey(key))
            throw new ArgumentException($"Unknown runtime config key: '{key}'");

        var result = await _repo.DeleteAsync(key);

        if (result)
        {
            lock (_cacheLock) { _cache = null; }
            _logger.LogInformation("Reset runtime config setting '{Key}' to default", key);
        }

        return result;
    }
}

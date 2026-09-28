using Api.Configuration;
using Api.Repositories;
using Api.Services.Caching;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Loads <see cref="RuntimeConfig"/> from the <c>site_settings</c> table.
/// Uses the shared <see cref="SingleFlightCache"/> with a TTL, the same as
/// <see cref="TenantSettingsService"/>.  The entry is removed on write.
/// </summary>
public interface ISiteSettingsService
{
    /// <summary>
    /// Get the current <see cref="RuntimeConfig"/>, with DB overrides applied
    /// on top of compiled defaults.  TTL-cached.
    /// </summary>
    Task<RuntimeConfig> GetRuntimeConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Update one or more runtime settings.  Returns the new resolved config.
    /// Only keys present in <see cref="RuntimeConfig.KeyMap"/> are accepted.
    /// </summary>
    Task<RuntimeConfig> UpdateRuntimeConfigAsync(Dictionary<string, string> updates, Guid? actorUserId, CancellationToken ct = default);

    /// <summary>
    /// Reset a runtime setting to its compiled default.
    /// </summary>
    Task<bool> ResetSettingAsync(string key, CancellationToken ct = default);
}

public sealed class SiteSettingsService : ISiteSettingsService
{
    private readonly ISiteSettingsRepository _repo;
    private readonly ILogger<SiteSettingsService> _logger;
    private readonly SingleFlightCache _cache;

    // One entry on the shared cache: the resolved config is the same answer for every caller.
    private const string CacheKey = "site-settings:runtime-config";
    private static readonly TimeSpan CacheTtl = TimePolicyConstants.CacheTtl;

    public SiteSettingsService(
        ISiteSettingsRepository repo,
        ILogger<SiteSettingsService> logger,
        SingleFlightCache cache)
    {
        _repo = repo;
        _logger = logger;
        _cache = cache;
    }

    public Task<RuntimeConfig> GetRuntimeConfigAsync(CancellationToken ct = default) =>
        // A failed read throws and caches nothing, rather than answering with compiled defaults.
        _cache.GetOrComputeAsync(CacheKey, CacheTtl, async () =>
        {
            var overrides = await _repo.GetAllAsync(ct);

            // Filter to only RuntimeConfig keys (site_settings may also contain
            // TenantSettings site-scoped overrides from the existing system).
            var runtimeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in overrides)
            {
                if (RuntimeConfig.KeyMap.ContainsKey(key))
                    runtimeOverrides[key] = value;
            }

            return RuntimeConfig.ApplyOverrides(runtimeOverrides);
        });

    public async Task<RuntimeConfig> UpdateRuntimeConfigAsync(Dictionary<string, string> updates, Guid? actorUserId, CancellationToken ct = default)
    {
        // Validate every key before writing any, then upsert in one statement, so an invalid
        // key late in the list cannot leave the earlier ones applied.
        var toUpsert = updates
            .Select(u =>
            {
                RuntimeConfig.ValidateValue(u.Key, u.Value);
                return (u.Key, u.Value, Category: RuntimeConfig.CategoryForKey(u.Key));
            })
            .ToList();
        await _repo.UpsertManyAsync(toUpsert, ct);

        _cache.Remove(CacheKey);

        _logger.LogInformation("Updated {Count} runtime config setting(s): {Keys}",
            updates.Count, string.Join(", ", updates.Keys));

        return await GetRuntimeConfigAsync(ct);
    }

    public async Task<bool> ResetSettingAsync(string key, CancellationToken ct = default)
    {
        if (!RuntimeConfig.KeyMap.ContainsKey(key))
            throw new ArgumentException($"Unknown runtime config key: '{key}'");

        var result = await _repo.DeleteAsync(key, ct);

        if (result)
        {
            _cache.Remove(CacheKey);
            _logger.LogInformation("Reset runtime config setting '{Key}' to default", key);
        }

        return result;
    }
}

using Api.Models;
using Api.Repositories;
using Api.Services.Caching;
using Orkyo.Shared;

namespace Api.Services;

public class TenantSettingsService : ITenantSettingsService
{
    private readonly ITenantSettingsRepository _tenantRepo;
    private readonly ISiteSettingsRepository _siteRepo;
    private readonly IOrgContextAccessor _orgContextAccessor;
    private readonly ILogger<TenantSettingsService> _logger;
    private readonly SingleFlightCache _cache;

    // Per-tenant entries keyed by TenantId, plus one entry for the site-level overrides that
    // every tenant shares. Invalidated on write; otherwise they expire after the TTL.
    private const string TenantKeyPrefix = "tenant-settings:";
    private const string SiteKey = "tenant-settings:site";
    private static readonly TimeSpan CacheTtl = TimePolicyConstants.CacheTtl;

    public TenantSettingsService(
        ITenantSettingsRepository tenantRepo,
        ISiteSettingsRepository siteRepo,
        IOrgContextAccessor orgContextAccessor,
        ILogger<TenantSettingsService> logger,
        SingleFlightCache cache)
    {
        _tenantRepo = tenantRepo;
        _siteRepo = siteRepo;
        _orgContextAccessor = orgContextAccessor;
        _logger = logger;
        _cache = cache;
    }

    /// <summary>True when operating in site-admin context (no tenant resolved for the request).</summary>
    private bool IsSiteContext => _orgContextAccessor.Current is null;

    /// <summary>The tenant's id; only valid after <see cref="IsSiteContext"/> was checked.</summary>
    private Guid TenantId => _orgContextAccessor.Current!.OrgId;

    public async Task<TenantSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            if (IsSiteContext)
            {
                // Site-admin context: only load site-scoped overrides from control_plane
                var siteOverrides = await GetSiteOverridesAsync();
                return TenantSettingsOverrideApplier.Apply(siteOverrides);
            }

            var tenantId = TenantId;

            if (_cache.TryGet<TenantSettings>(TenantKeyPrefix + tenantId, out var cached) && cached is not null)
            {
                return cached;
            }

            // Tenant context: only load tenant-scoped overrides from tenant DB
            var tenantOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                tenantOverrides = await _tenantRepo.GetAllAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load tenant settings for {TenantId}, using defaults", tenantId);
            }

            var settings = TenantSettingsOverrideApplier.Apply(tenantOverrides);
            _cache.Set(TenantKeyPrefix + tenantId, settings, CacheTtl);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load settings, using defaults");
            return TenantSettingsOverrideApplier.Defaults;
        }
    }

    public async Task<TenantSettings> UpdateSettingsAsync(Dictionary<string, string> updates, CancellationToken ct = default)
    {
        // Validate every key BEFORE writing any, then upsert in one atomic statement —
        // a mid-list invalid key can no longer leave a half-applied config behind.
        var toUpsert = new List<(string Key, string Value, string Category)>(updates.Count);
        foreach (var (key, value) in updates)
        {
            if (!TenantSettingDescriptorCatalog.ByKey.TryGetValue(key, out var descriptor))
            {
                throw new ArgumentException($"Unknown setting key: '{key}'");
            }

            TenantSettingsScopePolicy.EnsureWritableInScope(key, IsSiteContext, "modified");

            TenantSettingsValidator.Validate(descriptor, value);

            toUpsert.Add((key, value, descriptor.Category));
        }

        if (IsSiteContext)
        {
            await _siteRepo.UpsertManyAsync(toUpsert, ct);
        }
        else
        {
            await _tenantRepo.UpsertManyAsync(toUpsert, ct);
        }

        // Invalidate the relevant cache
        if (IsSiteContext)
        {
            _cache.Remove(SiteKey);
            _logger.LogInformation("Updated {Count} site-level settings: {Keys}",
                updates.Count, string.Join(", ", updates.Keys));
        }
        else
        {
            _cache.Remove(TenantKeyPrefix + TenantId);
            _logger.LogInformation("Tenant {TenantId} updated {Count} tenant-level settings: {Keys}",
                TenantId, updates.Count, string.Join(", ", updates.Keys));
        }

        return await GetSettingsAsync(ct);
    }

    public async Task<bool> ResetSettingAsync(string key, CancellationToken ct = default)
    {
        TenantSettingsScopePolicy.EnsureWritableInScope(key, IsSiteContext, "reset");

        bool result;

        if (IsSiteContext)
        {
            result = await _siteRepo.DeleteAsync(key, ct);
            if (result)
            {
                _cache.Remove(SiteKey);
                _logger.LogInformation("Reset site-level setting '{Key}' to default", key);
            }
        }
        else
        {
            result = await _tenantRepo.DeleteAsync(key, ct);
            if (result)
            {
                _cache.Remove(TenantKeyPrefix + TenantId);
                _logger.LogInformation("Tenant {TenantId} reset setting '{Key}' to default",
                    TenantId, key);
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
        // A copy per caller: the cached dictionary is shared, and TenantSettingsOverrideApplier's
        // callers are free to treat what they get back as their own.
        if (_cache.TryGet<Dictionary<string, string>>(SiteKey, out var cached) && cached is not null)
            return new Dictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);

        var overrides = await _siteRepo.GetAllAsync();
        _cache.Set(SiteKey, overrides, CacheTtl);
        return overrides;
    }
}

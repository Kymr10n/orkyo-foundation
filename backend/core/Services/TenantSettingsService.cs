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
        // Site-admin context: only load site-scoped overrides from control_plane
        if (IsSiteContext)
            return TenantSettingsOverrideApplier.Apply(await GetSiteOverridesAsync());

        // Tenant context: only load tenant-scoped overrides from tenant DB. A failed read
        // throws and caches nothing: compiled defaults pinned for the TTL used to turn branding
        // and auto-schedule off for every request after one database blip.
        return await _cache.GetOrComputeAsync(TenantKeyPrefix + TenantId, CacheTtl,
            async () => TenantSettingsOverrideApplier.Apply(await _tenantRepo.GetAllAsync(ct)));
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
        var cached = await _cache.GetOrComputeAsync(SiteKey, CacheTtl, () => _siteRepo.GetAllAsync());
        return new Dictionary<string, string>(cached, StringComparer.OrdinalIgnoreCase);
    }
}

using Api.Configuration;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Api.Services.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The settings services read through the shared <see cref="SingleFlightCache"/> and drop their
/// entry on every write. These pin the part a caller notices: a write is visible on the next
/// read, and a read that follows no write still costs one repository call.
/// </summary>
public class SettingsCacheInvalidationTests
{
    private static readonly string SiteKey = TenantSettingDescriptorCatalog.SiteScope[0].Key;

    private sealed class StubSiteRepository : ISiteSettingsRepository
    {
        public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int GetAllCalls;
        public bool DeleteResult = true;

        public Task<Dictionary<string, string>> GetAllAsync(CancellationToken ct = default)
        {
            GetAllCalls++;
            return Task.FromResult(new Dictionary<string, string>(Overrides, StringComparer.OrdinalIgnoreCase));
        }

        public Task UpsertAsync(string key, string value, string category, CancellationToken ct = default)
        {
            Overrides[key] = value;
            return Task.CompletedTask;
        }

        public Task UpsertManyAsync(
            IReadOnlyCollection<(string Key, string Value, string Category)> settings, CancellationToken ct = default)
        {
            foreach (var (key, value, _) in settings) Overrides[key] = value;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string key, CancellationToken ct = default)
        {
            Overrides.Remove(key);
            return Task.FromResult(DeleteResult);
        }
    }

    private sealed class NullOrgContextAccessor : IOrgContextAccessor
    {
        public OrgContext? Current => null;
    }

    private static (TenantSettingsService Service, StubSiteRepository Repo) BuildSiteContextService()
    {
        var repo = new StubSiteRepository();
        var service = new TenantSettingsService(
            Moq.Mock.Of<ITenantSettingsRepository>(),
            repo,
            new NullOrgContextAccessor(),
            NullLogger<TenantSettingsService>.Instance,
            new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())));
        return (service, repo);
    }

    [Fact]
    public async Task SiteOverrides_AreReadOnceAndThenServedFromCache()
    {
        var (service, repo) = BuildSiteContextService();

        await service.GetSettingsAsync();
        await service.GetSettingsAsync();

        Assert.Equal(1, repo.GetAllCalls);
    }

    [Fact]
    public async Task ASiteLevelUpdate_IsVisibleOnTheNextRead()
    {
        var (service, repo) = BuildSiteContextService();
        var value = NonDefaultValueFor(SiteKey);

        await service.GetSettingsAsync();
        await service.UpdateSettingsAsync(new Dictionary<string, string> { [SiteKey] = value });

        Assert.Equal(value, repo.Overrides[SiteKey]);
        Assert.True(repo.GetAllCalls > 1, "the write must drop the cached site overrides");
    }

    [Fact]
    public async Task ASiteLevelReset_DropsTheCachedOverrides()
    {
        var (service, repo) = BuildSiteContextService();
        await service.UpdateSettingsAsync(new Dictionary<string, string> { [SiteKey] = NonDefaultValueFor(SiteKey) });
        var callsBeforeReset = repo.GetAllCalls;

        var reset = await service.ResetSettingAsync(SiteKey);
        await service.GetSettingsAsync();

        Assert.True(reset);
        Assert.False(repo.Overrides.ContainsKey(SiteKey));
        Assert.True(repo.GetAllCalls > callsBeforeReset, "the reset must drop the cached site overrides");
    }

    [Fact]
    public async Task AFailedSiteLevelReset_LeavesTheCacheAlone()
    {
        var (service, repo) = BuildSiteContextService();
        repo.DeleteResult = false;
        await service.GetSettingsAsync();
        var callsBeforeReset = repo.GetAllCalls;

        var reset = await service.ResetSettingAsync(SiteKey);
        await service.GetSettingsAsync();

        Assert.False(reset);
        Assert.Equal(callsBeforeReset, repo.GetAllCalls);
    }

    [Fact]
    public async Task RuntimeConfig_IsReadOnce_AndReReadAfterAReset()
    {
        var repo = new StubSiteRepository();
        var service = new SiteSettingsService(
            repo,
            NullLogger<SiteSettingsService>.Instance,
            new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())));
        const string runtimeKey = "branding.branding_name";

        await service.GetRuntimeConfigAsync();
        await service.GetRuntimeConfigAsync();
        Assert.Equal(1, repo.GetAllCalls);

        var reset = await service.ResetSettingAsync(runtimeKey);
        await service.GetRuntimeConfigAsync();

        Assert.True(reset);
        Assert.Equal(2, repo.GetAllCalls);
    }

    [Fact]
    public async Task AFailedRuntimeConfigReset_LeavesTheCacheAlone()
    {
        var repo = new StubSiteRepository { DeleteResult = false };
        var service = new SiteSettingsService(
            repo,
            NullLogger<SiteSettingsService>.Instance,
            new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())));

        await service.GetRuntimeConfigAsync();
        var reset = await service.ResetSettingAsync("branding.branding_name");
        await service.GetRuntimeConfigAsync();

        Assert.False(reset);
        Assert.Equal(1, repo.GetAllCalls);
    }

    private sealed class FixedOrgContextAccessor : IOrgContextAccessor
    {
        public OrgContext? Current { get; } = new()
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "t",
            DbConnectionString = "Host=unused",
        };
    }

    [Fact]
    public async Task AFailedTenantSettingsRead_Throws_AndIsNotCached()
    {
        // Caching compiled defaults after one failed read turned branding and auto-schedule
        // off for the whole TTL. The failure must surface and the next read must retry.
        var tenantRepo = new Moq.Mock<ITenantSettingsRepository>();
        tenantRepo.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db blip"))
            .ReturnsAsync(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var service = new TenantSettingsService(
            tenantRepo.Object,
            new StubSiteRepository(),
            new FixedOrgContextAccessor(),
            NullLogger<TenantSettingsService>.Instance,
            new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSettingsAsync());
        await service.GetSettingsAsync();

        tenantRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Moq.Times.Exactly(2));
    }

    [Fact]
    public async Task ARuntimeConfigUpdate_WithOneInvalidValue_WritesNothing()
    {
        var repo = new StubSiteRepository();
        var service = new SiteSettingsService(
            repo,
            NullLogger<SiteSettingsService>.Instance,
            new SingleFlightCache(new MemoryCache(new MemoryCacheOptions())));

        var updates = new Dictionary<string, string>
        {
            ["branding.branding_name"] = "Acme",
            ["scheduling.holiday_provider_enabled"] = "not-a-bool",
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateRuntimeConfigAsync(updates, actorUserId: null));
        Assert.Empty(repo.Overrides);
    }

    /// <summary>A value the descriptor accepts that is not what it already defaults to.</summary>
    private static string NonDefaultValueFor(string key)
    {
        var descriptor = TenantSettingDescriptorCatalog.ByKey[key];
        if (bool.TryParse(descriptor.DefaultValue, out var flag)) return (!flag).ToString().ToLowerInvariant();
        if (int.TryParse(descriptor.DefaultValue, out var number)) return (number + 1).ToString();
        return descriptor.DefaultValue + "x";
    }
}

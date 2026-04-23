using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSettingDescriptorCatalogTests
{
    [Fact]
    public void All_ContainsKnownPlatformKeys()
    {
        var keys = TenantSettingDescriptorCatalog.All.Select(d => d.Key).ToHashSet();

        keys.Should().Contain("security.password_min_length");
        keys.Should().Contain("security.brute_force_lockout_threshold");
        keys.Should().Contain("invitations.invitation_expiry_days");
        keys.Should().Contain("uploads.upload_max_file_size_mb");
        keys.Should().Contain("uploads.upload_allowed_mime_types");
        keys.Should().Contain("search.search_default_page_size");
        keys.Should().Contain("branding.branding_product_name");
        keys.Should().Contain("scheduling.auto_schedule_enabled");
    }

    [Fact]
    public void All_DescriptorKeysAreUnique()
    {
        var keys = TenantSettingDescriptorCatalog.All.Select(d => d.Key).ToList();

        keys.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void SiteScope_OnlyContainsSiteScopedDescriptors()
    {
        TenantSettingDescriptorCatalog.SiteScope.Should().OnlyContain(d => d.Scope == "site");
    }

    [Fact]
    public void TenantScope_ExcludesSiteScopedDescriptors()
    {
        TenantSettingDescriptorCatalog.TenantScope.Should().NotContain(d => d.Scope == "site");
    }

    [Fact]
    public void SiteScopePlusTenantScope_EqualsAll()
    {
        var partitioned = TenantSettingDescriptorCatalog.SiteScope.Count
            + TenantSettingDescriptorCatalog.TenantScope.Count;

        partitioned.Should().Be(TenantSettingDescriptorCatalog.All.Count);
    }

    [Fact]
    public void SiteKeys_MatchesSiteScopeDescriptors()
    {
        var derived = TenantSettingDescriptorCatalog.SiteScope.Select(d => d.Key).ToHashSet();

        TenantSettingDescriptorCatalog.SiteKeys.Should().BeEquivalentTo(derived);
    }

    [Fact]
    public void ByKey_LookupIsCaseInsensitive()
    {
        TenantSettingDescriptorCatalog.ByKey.ContainsKey("SECURITY.PASSWORD_MIN_LENGTH").Should().BeTrue();
    }

    [Fact]
    public void DescriptorDefaultValues_MatchDefaultsMap()
    {
        foreach (var descriptor in TenantSettingDescriptorCatalog.All)
        {
            TenantSettingsKeyPolicy.DefaultsMap.Should().ContainKey(descriptor.Key);
            descriptor.DefaultValue.Should().Be(TenantSettingsKeyPolicy.DefaultsMap[descriptor.Key]);
        }
    }

    [Fact]
    public void AutoScheduleEnabled_IsTenantScoped()
    {
        var descriptor = TenantSettingDescriptorCatalog.ByKey["scheduling.auto_schedule_enabled"];
        descriptor.Scope.Should().NotBe("site");
    }

    [Fact]
    public void SecurityPasswordMinLength_IsSiteScoped()
    {
        var descriptor = TenantSettingDescriptorCatalog.ByKey["security.password_min_length"];
        descriptor.Scope.Should().Be("site");
    }
}

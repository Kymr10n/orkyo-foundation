using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSettingsScopePolicyTests
{
    // Real keys from TenantSettingDescriptorCatalog.
    private const string SiteKey = "security.password_min_length";
    private const string TenantKey = "branding.branding_product_name";

    [Fact]
    public void EnsureWritableInScope_SiteKeyFromSiteContext_DoesNotThrow()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(SiteKey, isSiteContext: true, "modified");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureWritableInScope_TenantKeyFromTenantContext_DoesNotThrow()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(TenantKey, isSiteContext: false, "modified");
        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureWritableInScope_TenantKeyFromSiteContext_ThrowsWithWireMessage()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(TenantKey, isSiteContext: true, "modified");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Setting '{TenantKey}' is tenant-scoped and cannot be modified from the site context");
    }

    [Fact]
    public void EnsureWritableInScope_SiteKeyFromTenantContext_ThrowsWithWireMessage()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(SiteKey, isSiteContext: false, "modified");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Setting '{SiteKey}' is site-scoped and cannot be modified from a tenant context");
    }

    [Fact]
    public void EnsureWritableInScope_OperationVerbEchoedInMessage_Reset()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(TenantKey, isSiteContext: true, "reset");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Setting '{TenantKey}' is tenant-scoped and cannot be reset from the site context");
    }

    [Fact]
    public void EnsureWritableInScope_OperationVerbEchoedInMessage_ResetSiteFromTenant()
    {
        var act = () => TenantSettingsScopePolicy.EnsureWritableInScope(SiteKey, isSiteContext: false, "reset");

        act.Should().Throw<ArgumentException>()
            .WithMessage($"Setting '{SiteKey}' is site-scoped and cannot be reset from a tenant context");
    }
}

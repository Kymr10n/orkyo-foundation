using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSettingsKeyPolicyTests
{
    [Theory]
    [InlineData("PasswordMinLength", "security.password_min_length")]
    [InlineData("BruteForce_LockoutThreshold", "security.brute_force_lockout_threshold")]
    [InlineData("Upload_MaxFileSizeMb", "uploads.upload_max_file_size_mb")]
    [InlineData("Search_DefaultPageSize", "search.search_default_page_size")]
    [InlineData("Branding_ProductName", "branding.branding_product_name")]
    [InlineData("AutoSchedule_Enabled", "scheduling.auto_schedule_enabled")]
    [InlineData("Invitation_ExpiryDays", "invitations.invitation_expiry_days")]
    [InlineData("RateLimit_LoginPerMinute", "security.rate_limit_login_per_minute")]
    public void PropertyToKey_ShouldProduceDotSeparatedSnakeCaseWithCategory(string propertyName, string expected)
    {
        TenantSettingsKeyPolicy.PropertyToKey(propertyName).Should().Be(expected);
    }

    [Theory]
    [InlineData("PasswordMinLength", "security")]
    [InlineData("BruteForce_LockoutThreshold", "security")]
    [InlineData("RateLimit_LoginPerMinute", "security")]
    [InlineData("Invitation_ExpiryDays", "invitations")]
    [InlineData("Upload_MaxFileSizeMb", "uploads")]
    [InlineData("Search_DefaultPageSize", "search")]
    [InlineData("Branding_ProductName", "branding")]
    [InlineData("AutoSchedule_Enabled", "scheduling")]
    [InlineData("SomeUnrelatedThing", "general")]
    public void GetCategory_ShouldRouteByPropertyPrefix(string propertyName, string expected)
    {
        TenantSettingsKeyPolicy.GetCategory(propertyName).Should().Be(expected);
    }

    [Theory]
    [InlineData("PasswordMinLength", "password_min_length")]
    [InlineData("BruteForce_LockoutThreshold", "brute_force_lockout_threshold")]
    [InlineData("Upload_MaxFileSizeMb", "upload_max_file_size_mb")]
    [InlineData("Already_Snake_Case", "already_snake_case")]
    [InlineData("A", "a")]
    public void ToSnakeCase_ShouldLowercaseAndPreserveExistingUnderscores(string input, string expected)
    {
        TenantSettingsKeyPolicy.ToSnakeCase(input).Should().Be(expected);
    }

    [Fact]
    public void KeyToProperty_ShouldContainEveryTenantSettingsProperty()
    {
        var props = typeof(TenantSettings).GetProperties();
        TenantSettingsKeyPolicy.KeyToProperty.Count.Should().Be(props.Length);
        foreach (var prop in props)
        {
            var key = TenantSettingsKeyPolicy.PropertyToKey(prop.Name);
            TenantSettingsKeyPolicy.KeyToProperty.Should().ContainKey(key);
            TenantSettingsKeyPolicy.KeyToProperty[key].Name.Should().Be(prop.Name);
        }
    }

    [Fact]
    public void KeyToProperty_ShouldBeCaseInsensitive()
    {
        var anyKey = TenantSettingsKeyPolicy.KeyToProperty.Keys.First();
        TenantSettingsKeyPolicy.KeyToProperty.ContainsKey(anyKey.ToUpperInvariant()).Should().BeTrue();
    }

    [Fact]
    public void DefaultsMap_ShouldStringifyEveryDefaultValue()
    {
        var defaults = new TenantSettings();
        TenantSettingsKeyPolicy.DefaultsMap.Count.Should().Be(typeof(TenantSettings).GetProperties().Length);
        foreach (var prop in typeof(TenantSettings).GetProperties())
        {
            var key = TenantSettingsKeyPolicy.PropertyToKey(prop.Name);
            var expected = Convert.ToString(prop.GetValue(defaults), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            TenantSettingsKeyPolicy.DefaultsMap[key].Should().Be(expected);
        }
    }
}

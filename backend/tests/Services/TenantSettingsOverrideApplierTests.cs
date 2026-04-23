using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantSettingsOverrideApplierTests
{
    [Fact]
    public void Apply_WithEmptyOverrides_ReturnsDefaultsInstance()
    {
        var result = TenantSettingsOverrideApplier.Apply(new Dictionary<string, string>());

        result.Should().BeSameAs(TenantSettingsOverrideApplier.Defaults);
    }

    [Fact]
    public void Apply_WithUnknownKeysOnly_ReturnsDefaultsInstance()
    {
        var overrides = new Dictionary<string, string> { ["nonsense.unknown_key"] = "value" };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.Should().BeSameAs(TenantSettingsOverrideApplier.Defaults);
    }

    [Fact]
    public void Apply_WithIntOverride_AppliesParsedValue()
    {
        var overrides = new Dictionary<string, string>
        {
            ["security.password_min_length"] = "16",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.PasswordMinLength.Should().Be(16);
    }

    [Fact]
    public void Apply_WithBoolOverride_AppliesParsedValue()
    {
        var overrides = new Dictionary<string, string>
        {
            ["scheduling.auto_schedule_enabled"] = "true",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.AutoSchedule_Enabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_WithDoubleOverride_AppliesInvariantParsedValue()
    {
        var overrides = new Dictionary<string, string>
        {
            ["search.search_primary_similarity_threshold"] = "0.45",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.Search_PrimarySimilarityThreshold.Should().Be(0.45);
    }

    [Fact]
    public void Apply_WithStringOverride_AppliesValue()
    {
        var overrides = new Dictionary<string, string>
        {
            ["branding.branding_product_name"] = "Custom Product",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.Branding_ProductName.Should().Be("Custom Product");
    }

    [Fact]
    public void Apply_WithUnparseableValue_FallsBackToDefaultForThatProperty()
    {
        var overrides = new Dictionary<string, string>
        {
            ["security.password_min_length"] = "not-an-integer",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        // Falls back to default since the value couldn't be converted; whole record == defaults
        result.Should().BeSameAs(TenantSettingsOverrideApplier.Defaults);
    }

    [Fact]
    public void Apply_PreservesNonOverriddenPropertiesAtDefault()
    {
        var overrides = new Dictionary<string, string>
        {
            ["security.password_min_length"] = "20",
        };

        var result = TenantSettingsOverrideApplier.Apply(overrides);

        result.PasswordMinLength.Should().Be(20);
        // Untouched properties retain default values
        var defaults = new TenantSettings();
        result.Branding_ProductName.Should().Be(defaults.Branding_ProductName);
        result.AutoSchedule_Enabled.Should().Be(defaults.AutoSchedule_Enabled);
    }

    [Theory]
    [InlineData(typeof(int), "42", 42)]
    [InlineData(typeof(double), "3.14", 3.14)]
    [InlineData(typeof(bool), "true", true)]
    [InlineData(typeof(string), "hello", "hello")]
    public void ConvertValue_KnownTypes_ReturnParsedValue(Type targetType, string raw, object expected)
    {
        TenantSettingsOverrideApplier.ConvertValue(targetType, raw).Should().Be(expected);
    }

    [Fact]
    public void ConvertValue_UnsupportedType_ReturnsNull()
    {
        TenantSettingsOverrideApplier.ConvertValue(typeof(Guid), Guid.NewGuid().ToString()).Should().BeNull();
    }

    [Fact]
    public void ConvertValue_BadInt_ReturnsNull()
    {
        TenantSettingsOverrideApplier.ConvertValue(typeof(int), "abc").Should().BeNull();
    }
}

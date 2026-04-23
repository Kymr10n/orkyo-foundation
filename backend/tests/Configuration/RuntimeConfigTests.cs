using Api.Configuration;

namespace Orkyo.Foundation.Tests.Configuration;

public class RuntimeConfigTests
{
    [Fact]
    public void Defaults_ShouldExposeLockedDefaultValues()
    {
        var d = RuntimeConfig.Defaults;
        d.DefaultTimezone.Should().Be("UTC");
        d.WorkingHoursStart.Should().Be("08:00");
        d.WorkingHoursEnd.Should().Be("18:00");
        d.HolidayProviderEnabled.Should().BeFalse();
        d.BrandingName.Should().Be("Orkyo");
        d.BrandingLogoUrl.Should().Be("");
    }

    [Fact]
    public void KeyMap_ShouldContainEveryConfigurableProperty()
    {
        RuntimeConfig.KeyMap["general.default_timezone"].Should().Be(nameof(RuntimeConfig.DefaultTimezone));
        RuntimeConfig.KeyMap["scheduling.working_hours_start"].Should().Be(nameof(RuntimeConfig.WorkingHoursStart));
        RuntimeConfig.KeyMap["scheduling.working_hours_end"].Should().Be(nameof(RuntimeConfig.WorkingHoursEnd));
        RuntimeConfig.KeyMap["scheduling.holiday_provider_enabled"].Should().Be(nameof(RuntimeConfig.HolidayProviderEnabled));
        RuntimeConfig.KeyMap["branding.branding_name"].Should().Be(nameof(RuntimeConfig.BrandingName));
        RuntimeConfig.KeyMap["branding.branding_logo_url"].Should().Be(nameof(RuntimeConfig.BrandingLogoUrl));
        RuntimeConfig.KeyMap.Should().HaveCount(6);
    }

    [Fact]
    public void KeyMap_ShouldBeCaseInsensitive()
    {
        RuntimeConfig.KeyMap.ContainsKey("BRANDING.BRANDING_NAME").Should().BeTrue();
    }

    [Fact]
    public void PropertyToKeyMap_ShouldBeReverseOfKeyMap()
    {
        foreach (var (dbKey, propName) in RuntimeConfig.KeyMap)
        {
            RuntimeConfig.PropertyToKeyMap[propName].Should().Be(dbKey);
        }
    }

    [Theory]
    [InlineData("general.default_timezone", "general")]
    [InlineData("scheduling.working_hours_start", "scheduling")]
    [InlineData("branding.branding_logo_url", "branding")]
    [InlineData("nodot", "general")]
    public void CategoryForKey_ShouldReturnPrefixOrFallbackToGeneral(string key, string expected)
    {
        RuntimeConfig.CategoryForKey(key).Should().Be(expected);
    }

    [Fact]
    public void ApplyOverrides_ShouldReturnDefaults_WhenNoOverridesProvided()
    {
        RuntimeConfig.ApplyOverrides(new()).Should().BeSameAs(RuntimeConfig.Defaults);
    }

    [Fact]
    public void ApplyOverrides_ShouldOverrideKnownStringKeys()
    {
        var c = RuntimeConfig.ApplyOverrides(new()
        {
            ["general.default_timezone"] = "Europe/Berlin",
            ["branding.branding_name"] = "Acme",
        });

        c.DefaultTimezone.Should().Be("Europe/Berlin");
        c.BrandingName.Should().Be("Acme");
        c.WorkingHoursStart.Should().Be("08:00"); // unchanged default
    }

    [Fact]
    public void ApplyOverrides_ShouldParseHolidayProviderEnabledAsBool()
    {
        RuntimeConfig.ApplyOverrides(new() { ["scheduling.holiday_provider_enabled"] = "true" })
            .HolidayProviderEnabled.Should().BeTrue();

        RuntimeConfig.ApplyOverrides(new() { ["scheduling.holiday_provider_enabled"] = "not-a-bool" })
            .HolidayProviderEnabled.Should().BeFalse();
    }

    [Fact]
    public void ApplyOverrides_ShouldIgnoreUnknownKeys()
    {
        var c = RuntimeConfig.ApplyOverrides(new() { ["unknown.key"] = "x" });
        c.Should().BeEquivalentTo(new RuntimeConfig());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateValue_ShouldRejectEmptyOrWhitespace(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("general.default_timezone", value);
        act.Should().Throw<ArgumentException>().WithMessage("*cannot be empty*");
    }

    [Fact]
    public void ValidateValue_ShouldRejectValuesOver500Chars()
    {
        var value = new string('a', RuntimeConfig.MaxValueLength + 1);
        var act = () => RuntimeConfig.ValidateValue("general.default_timezone", value);
        act.Should().Throw<ArgumentException>().WithMessage($"*exceeds maximum length of {RuntimeConfig.MaxValueLength} characters*");
    }

    [Fact]
    public void ValidateValue_ShouldRejectUnknownKey()
    {
        var act = () => RuntimeConfig.ValidateValue("not.a.key", "x");
        act.Should().Throw<ArgumentException>().WithMessage("*Unknown runtime config key*");
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("nope")]
    public void ValidateValue_ShouldRejectNonBoolForHolidayProviderEnabled(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("scheduling.holiday_provider_enabled", value);
        act.Should().Throw<ArgumentException>().WithMessage("*'true' or 'false'*");
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("True")]
    public void ValidateValue_ShouldAcceptBoolForHolidayProviderEnabled(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("scheduling.holiday_provider_enabled", value);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("scheduling.working_hours_start", "08:00")]
    [InlineData("scheduling.working_hours_end", "18:30")]
    public void ValidateValue_ShouldAcceptValidTimeForWorkingHours(string key, string value)
    {
        var act = () => RuntimeConfig.ValidateValue(key, value);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("scheduling.working_hours_start", "25:99")]
    [InlineData("scheduling.working_hours_end", "notatime")]
    public void ValidateValue_ShouldRejectInvalidTimeForWorkingHours(string key, string value)
    {
        var act = () => RuntimeConfig.ValidateValue(key, value);
        act.Should().Throw<ArgumentException>().WithMessage("*valid time*");
    }

    [Fact]
    public void ValidateValue_ShouldRejectBrandingNameOver100Chars()
    {
        var value = new string('a', RuntimeConfig.MaxBrandingNameLength + 1);
        var act = () => RuntimeConfig.ValidateValue("branding.branding_name", value);
        act.Should().Throw<ArgumentException>().WithMessage($"*not exceed {RuntimeConfig.MaxBrandingNameLength} characters*");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("Acme <b>Co</b>")]
    public void ValidateValue_ShouldRejectBrandingNameWithHtmlTags(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("branding.branding_name", value);
        act.Should().Throw<ArgumentException>().WithMessage("*HTML tags*");
    }

    [Fact]
    public void ValidateValue_ShouldAcceptPlainBrandingName()
    {
        var act = () => RuntimeConfig.ValidateValue("branding.branding_name", "Acme & Co");
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("https://example.com/logo.png")]
    [InlineData("http://localhost:8080/logo.svg")]
    public void ValidateValue_ShouldAcceptAbsoluteUrlForBrandingLogoUrl(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("branding.branding_logo_url", value);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("relative/path.png")]
    public void ValidateValue_ShouldRejectNonAbsoluteUrlForBrandingLogoUrl(string value)
    {
        var act = () => RuntimeConfig.ValidateValue("branding.branding_logo_url", value);
        act.Should().Throw<ArgumentException>().WithMessage("*valid URL*");
    }
}

using Api.Models;
using Api.Services;
using FluentAssertions;
using Xunit;

namespace Api.Tests.Models;

public class TenantSettingsTests
{
    [Fact]
    public void DefaultPasswordMinLength_Constant_ShouldBe8()
    {
        TenantSettings.DefaultPasswordMinLength.Should().Be(8);
    }

    [Fact]
    public void TenantSettings_ShouldExposeExpectedSecurityDefaults()
    {
        var settings = new TenantSettings();

        settings.PasswordMinLength.Should().Be(8);
        settings.BruteForce_LockoutThreshold.Should().Be(10);
        settings.BruteForce_BaseLockoutMinutes.Should().Be(15);
        settings.BruteForce_MaxLockoutMinutes.Should().Be(120);
        settings.BruteForce_FailureWindowMinutes.Should().Be(30);
    }

    [Fact]
    public void TenantSettings_ShouldExposeExpectedRateLimitDefaults()
    {
        var settings = new TenantSettings();

        settings.RateLimit_LoginPerMinute.Should().Be(5);
        settings.RateLimit_RegisterPerHour.Should().Be(3);
        settings.RateLimit_DefaultPerMinute.Should().Be(60);
        settings.RateLimit_WritePerMinute.Should().Be(10);
    }

    [Fact]
    public void TenantSettings_ShouldExposeExpectedInvitationUploadAndSearchDefaults()
    {
        var settings = new TenantSettings();

        settings.Invitation_ExpiryDays.Should().Be(7);
        settings.Upload_MaxFileSizeMb.Should().Be(10);
        settings.Upload_AllowedMimeTypes.Should().Be("image/png,image/jpeg,image/jpg");
        settings.Search_DefaultPageSize.Should().Be(20);
        settings.Search_PrimarySimilarityThreshold.Should().Be(0.2);
        settings.Search_SecondarySimilarityThreshold.Should().Be(0.15);
    }

    [Fact]
    public void TenantSettings_ShouldExposeExpectedBrandingAndAutoScheduleDefaults()
    {
        var settings = new TenantSettings();

        settings.Branding_ProductName.Should().Be("Orkyo");
        settings.Branding_PrimaryColor.Should().Be("#667eea");
        settings.Branding_SecondaryColor.Should().Be("#764ba2");
        settings.AutoSchedule_Enabled.Should().BeFalse();
    }

    [Fact]
    public void ToEmailBranding_ShouldProjectBrandingFields()
    {
        var settings = new TenantSettings
        {
            Branding_ProductName = "Acme",
            Branding_PrimaryColor = "#111111",
            Branding_SecondaryColor = "#222222"
        };

        var branding = settings.ToEmailBranding();

        branding.ProductName.Should().Be("Acme");
        branding.PrimaryColor.Should().Be("#111111");
        branding.SecondaryColor.Should().Be("#222222");
    }

    [Fact]
    public void EmailBranding_Default_ShouldMatchTenantSettingsDefaults()
    {
        var defaults = new TenantSettings();

        EmailBranding.Default.ProductName.Should().Be(defaults.Branding_ProductName);
        EmailBranding.Default.PrimaryColor.Should().Be(defaults.Branding_PrimaryColor);
        EmailBranding.Default.SecondaryColor.Should().Be(defaults.Branding_SecondaryColor);
    }

    [Fact]
    public void TenantSettingDescriptor_ShouldDefaultScopeToTenant()
    {
        var descriptor = new TenantSettingDescriptor(
            Key: "search.defaultPageSize",
            Category: "Search",
            DisplayName: "Default Page Size",
            Description: "Default search result size",
            ValueType: "int",
            DefaultValue: "20");

        descriptor.Scope.Should().Be("tenant");
        descriptor.MinValue.Should().BeNull();
        descriptor.MaxValue.Should().BeNull();
    }

    [Fact]
    public void TenantSettingDescriptor_ShouldPreserveExplicitOptionalValues()
    {
        var descriptor = new TenantSettingDescriptor(
            Key: "security.passwordMinLength",
            Category: "Security",
            DisplayName: "Password Length",
            Description: "Minimum password length",
            ValueType: "int",
            DefaultValue: "8",
            Scope: "site",
            MinValue: "8",
            MaxValue: "128");

        descriptor.Scope.Should().Be("site");
        descriptor.MinValue.Should().Be("8");
        descriptor.MaxValue.Should().Be("128");
    }
}

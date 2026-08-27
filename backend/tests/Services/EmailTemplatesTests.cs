using Api.Services;
using AwesomeAssertions;
using Xunit;

namespace Api.Tests.Services;

public class EmailTemplatesTests
{
    private static readonly EmailBranding CustomBranding = new(
        "Acme",
        "#111111",
        "#222222");

    [Fact]
    public void GetWelcomeEmail_ShouldUseCustomBrandingInSubjectAndBodies()
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetWelcomeEmail("Alex", CustomBranding);

        subject.Should().Be("Welcome to Acme!");
        htmlBody.Should().Contain("Hi Alex,");
        htmlBody.Should().Contain("using Acme");
        htmlBody.Should().Contain("#111111");
        textBody.Should().Contain("using Acme to manage your resources efficiently");
        textBody.Should().Contain("Best regards,\nAcme");
    }

    [Fact]
    public void GetWelcomeEmail_ShouldContainGettingStartedContent()
    {
        var (_, htmlBody, textBody) = EmailTemplates.GetWelcomeEmail("Alex");

        htmlBody.Should().Contain("Getting Started");
        htmlBody.Should().Contain("Create your first site and spaces");
        textBody.Should().Contain("Create your first site and spaces");
        textBody.Should().Contain("Invite team members to collaborate");
    }

    [Fact]
    public void GetEmailChangeConfirmationEmail_ShouldUseDefaultBranding_WhenBrandingNotProvided()
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetEmailChangeConfirmationEmail(
            "Alex", "https://app.test/confirm-email?token=abc");

        subject.Should().Be("Confirm your new email address");
        htmlBody.Should().Contain("Confirm Your New Email");
        htmlBody.Should().Contain("Hi Alex,");
        htmlBody.Should().Contain("https://app.test/confirm-email?token=abc");
        htmlBody.Should().Contain("expire in 24 hours");
        htmlBody.Should().Contain(BrandTokens.GradientFrom);
        textBody.Should().Contain("https://app.test/confirm-email?token=abc");
        textBody.Should().Contain("expire in 24 hours");
    }

    [Fact]
    public void GetEmailChangeConfirmationEmail_ShouldApplyCustomBranding()
    {
        var (_, htmlBody, textBody) = EmailTemplates.GetEmailChangeConfirmationEmail(
            "Alex", "https://app.test/confirm-email?token=abc", CustomBranding);

        htmlBody.Should().Contain("your Acme account");
        htmlBody.Should().Contain("#111111");
        htmlBody.Should().Contain("#222222");
        textBody.Should().Contain("your Acme account");
        textBody.Should().Contain("Best regards,\nAcme");
    }

    // ── Lifecycle / admin / security templates (added 2026-06) ──────────────────

    private static void AssertBranded(string subject, string html, string text)
    {
        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().Contain("Acme");        // branding product name substituted
        html.Should().Contain("#111111");     // primary colour substituted
        text.Should().Contain("Acme");
        text.Should().Contain("Best regards");
    }

    [Fact]
    public void TenantInactivityWarning_hasLoginCtaAndDays()
    {
        var (s, h, t) = EmailTemplates.GetTenantInactivityWarningEmail("Acme HQ", "https://app/x", 7, CustomBranding);
        s.Should().Contain("Acme HQ");
        h.Should().Contain("https://app/x").And.Contain("7 days");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TenantSuspended_hasReactivateLink()
    {
        var (s, h, t) = EmailTemplates.GetTenantSuspendedEmail("Acme HQ", "https://app/react", 90, CustomBranding);
        s.Should().Contain("suspended");
        h.Should().Contain("https://app/react").And.Contain("90 days");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TenantDeletingWarning_hasRestoreLinkAndFooter()
    {
        var (s, h, t) = EmailTemplates.GetTenantDeletingWarningEmail("Acme HQ", "https://app/restore", 7, CustomBranding);
        s.Should().Contain("permanently deleted");
        h.Should().Contain("https://app/restore").And.Contain("7 days");
        h.Should().Contain("no action is needed"); // footer branch
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TenantDeleted_isInformational()
    {
        var (s, h, t) = EmailTemplates.GetTenantDeletedEmail("Acme HQ", CustomBranding);
        s.Should().Contain("deleted");
        h.Should().Contain("contact us");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TenantWelcome_hasOpenCta()
    {
        var (s, h, t) = EmailTemplates.GetTenantWelcomeEmail("Acme HQ", "https://app", CustomBranding);
        s.Should().Contain("Acme HQ");
        h.Should().Contain("https://app").And.Contain("owner");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void RoleChanged_namesTheNewRole()
    {
        var (s, h, t) = EmailTemplates.GetRoleChangedEmail("Acme HQ", "editor", "https://app", CustomBranding);
        s.Should().Contain("role");
        h.Should().Contain("editor").And.Contain("https://app");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void MemberRemoved_isInformational()
    {
        var (s, h, t) = EmailTemplates.GetMemberRemovedEmail("Acme HQ", CustomBranding);
        s.Should().Contain("removed");
        h.Should().Contain("Acme HQ");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void Ownership_receivedAndTransferred()
    {
        var (s1, h1, t1) = EmailTemplates.GetOwnershipReceivedEmail("Acme HQ", "https://app", CustomBranding);
        s1.Should().Contain("owner");
        h1.Should().Contain("https://app");
        AssertBranded(s1, h1, t1);

        var (s2, h2, t2) = EmailTemplates.GetOwnershipTransferredEmail("Acme HQ", "new@x.com", CustomBranding);
        s2.Should().Contain("transferred");
        h2.Should().Contain("new@x.com");
        AssertBranded(s2, h2, t2);
    }

    [Fact]
    public void QuotaLimitReached_hasLimitAndManageLink()
    {
        var (s, h, t) = EmailTemplates.GetQuotaLimitReachedEmail("Acme HQ", "active seats", 25, "https://app/limits", CustomBranding);
        s.Should().Contain("active seats");
        h.Should().Contain("25").And.Contain("https://app/limits");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TierChanged_namesPlan()
    {
        var (s, h, t) = EmailTemplates.GetTierChangedEmail("Acme HQ", "professional", "https://app", CustomBranding);
        s.Should().Contain("plan");
        h.Should().Contain("professional").And.Contain("https://app");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void PasswordChanged_hasSecurityFooter()
    {
        var (s, h, t) = EmailTemplates.GetPasswordChangedEmail("Dana", CustomBranding);
        s.Should().Contain("password");
        h.Should().Contain("Dana").And.Contain("contact us");
        AssertBranded(s, h, t);
    }

    [Theory]
    [InlineData(true, "enabled")]
    [InlineData(false, "disabled")]
    public void MfaChanged_reflectsState(bool enabled, string word)
    {
        var (s, h, t) = EmailTemplates.GetMfaChangedEmail("Dana", enabled, CustomBranding);
        s.Should().Contain(word);
        h.Should().Contain(word).And.Contain("Dana");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void EmailChange_requestedOldAddressAndChanged()
    {
        var (s1, h1, t1) = EmailTemplates.GetEmailChangeRequestedOldAddressEmail("Dana", "new@x.com", CustomBranding);
        s1.Should().Contain("email change");
        h1.Should().Contain("new@x.com").And.Contain("contact us");
        AssertBranded(s1, h1, t1);

        var (s2, h2, t2) = EmailTemplates.GetEmailChangedEmail("Dana", "new@x.com", CustomBranding);
        s2.Should().Contain("changed");
        h2.Should().Contain("new@x.com");
        AssertBranded(s2, h2, t2);
    }
    [Fact]
    public void GetAnnouncementEmail_IncludesTitleBodyAndUnsubscribeLink()
    {
        var (subject, html, text) = EmailTemplates.GetAnnouncementEmail(
            "Scheduled maintenance", "Servers down Friday.", isImportant: false,
            "https://app.test/api/announcements/unsubscribe?token=abc");

        subject.Should().Be("Scheduled maintenance");
        html.Should().Contain("Servers down Friday.")
            .And.Contain("https://app.test/api/announcements/unsubscribe?token=abc")
            .And.Contain("Unsubscribe");
        text.Should().Contain("Servers down Friday.")
            .And.Contain("https://app.test/api/announcements/unsubscribe?token=abc");
    }

    [Fact]
    public void GetAnnouncementEmail_ImportantPrefixesSubjectAndShowsMandatoryNotice()
    {
        var (subject, html, text) = EmailTemplates.GetAnnouncementEmail(
            "Security notice", "Please reset your password.", isImportant: true,
            "https://app.test/api/announcements/unsubscribe?token=x");

        subject.Should().Be("[Important] Security notice");
        // Important announcements are mandatory: no unsubscribe link, a mandatory-notice instead.
        html.Should().Contain("regardless of email preferences")
            .And.NotContain("Unsubscribe from announcement emails")
            .And.NotContain("token=x");
        text.Should().Contain("regardless of email preferences")
            .And.NotContain("token=x");
    }

    // ── HTML-encoding of user-supplied values (XSS regression) ──────────────────

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    public void UserSuppliedValues_AreHtmlEncoded_AcrossTemplates(string hostile)
    {
        var samples = new[]
        {
            EmailTemplates.GetWelcomeEmail(hostile).htmlBody,
            EmailTemplates.GetLifecycleWarningEmail(hostile, "https://x/confirm", 1).htmlBody,
            EmailTemplates.GetDormancyNoticeEmail(hostile).htmlBody,
            EmailTemplates.GetNewUserAlertEmail("a@x.com", hostile).htmlBody,
            EmailTemplates.GetNewTenantAlertEmail("slug", hostile, "o@x.com").htmlBody,
            EmailTemplates.GetTenantSuspendedEmail(hostile, "https://x", 30).htmlBody,
            EmailTemplates.GetOwnershipTransferredEmail("Tenant", hostile).htmlBody,
            EmailTemplates.GetEmailChangedEmail("Alex", hostile).htmlBody,
            EmailTemplates.GetAnnouncementEmail(hostile, hostile, false, "https://x/unsub").htmlBody,
            EmailTemplates.GetDesignPartnerConfirmationEmail(hostile).htmlBody,
        };

        foreach (var html in samples)
        {
            html.Should().NotContain(hostile, "user input must be HTML-encoded before interpolation");
            html.Should().Contain("&lt;", "the encoded form should appear instead");
        }
    }

    [Fact]
    public void Layout_FixedChrome_UsesBrandTokens()
    {
        var (_, html, _) = EmailTemplates.GetWelcomeEmail("Alex");

        html.Should().Contain(BrandTokens.FontStack)
            .And.Contain(BrandTokens.Text)
            .And.Contain(BrandTokens.PanelBg)
            .And.NotContain("Arial")
            .And.NotContain("#667eea");
    }

    [Fact]
    public void Layout_CtaUsesTheActionAccent_NotTheHeaderChrome()
    {
        // The CTA must not inherit the tenant's header-gradient color. That color is dark
        // chrome, and a near-black button vanished entirely once an email client
        // auto-darkened the light card (reported from Thunderbird in dark mode).
        var (_, html, _) = EmailTemplates.GetTenantInactivityWarningEmail("Peta", "https://peta.orkyo.com", 7);

        html.Should().Contain($"background-color: {BrandTokens.Accent}");
        html.Should().NotContain($"background-color: {BrandTokens.GradientFrom};",
            "the header chrome color must never back an action element");
    }

    [Fact]
    public void Layout_DeclaresDarkModeSupportAndOverrides()
    {
        var (_, html, _) = EmailTemplates.GetTenantInactivityWarningEmail("Peta", "https://peta.orkyo.com", 7);

        // Clients that honour the switch render the designed dark email instead of
        // auto-inverting the light one.
        html.Should().Contain(@"name=""color-scheme"" content=""light dark""")
            .And.Contain(@"name=""supported-color-schemes"" content=""light dark""")
            .And.Contain("@media (prefers-color-scheme: dark)");

        // The overrides must beat the inline styles, which is why they carry !important.
        html.Should().Contain($"background-color: {BrandTokens.DarkPanelBg} !important")
            .And.Contain($"color: {BrandTokens.DarkText} !important");
    }

    [Fact]
    public void GetDesignPartnerConfirmationEmail_IsBrandedAndPersonalized()
    {
        var (subject, html, text) = EmailTemplates.GetDesignPartnerConfirmationEmail("Dana");

        subject.Should().Contain("Design Partner application was received");
        html.Should().Contain("Hi Dana,").And.Contain("<!DOCTYPE html>");
        text.Should().Contain("Design Partner Program");
    }
}

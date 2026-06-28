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
    public void GetVerificationEmail_ShouldUseDefaultBranding_WhenBrandingNotProvided()
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetVerificationEmail("Alex", "https://app.test/verify");

        subject.Should().Be("Verify your email address");
        htmlBody.Should().Contain("Welcome to Orkyo!");
        htmlBody.Should().Contain("#667eea");
        htmlBody.Should().Contain("#764ba2");
        htmlBody.Should().Contain("https://app.test/verify");
        textBody.Should().Contain("Welcome to Orkyo!");
        textBody.Should().Contain("https://app.test/verify");
    }

    [Fact]
    public void GetVerificationEmail_ShouldApplyCustomBrandingAndGreeting()
    {
        var (_, htmlBody, textBody) = EmailTemplates.GetVerificationEmail("Alex", "https://app.test/verify", CustomBranding);

        htmlBody.Should().Contain("Welcome to Acme!");
        htmlBody.Should().Contain("Hi Alex,");
        htmlBody.Should().Contain("#111111");
        htmlBody.Should().Contain("#222222");
        textBody.Should().Contain("Thank you for registering with Acme.");
        textBody.Should().Contain("The Acme Team");
    }

    [Fact]
    public void GetPasswordResetEmail_ShouldReturnExpectedSubjectAndLinkInBothBodies()
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetPasswordResetEmail("Alex", "https://app.test/reset", CustomBranding);

        subject.Should().Be("Reset your password");
        htmlBody.Should().Contain("Password Reset Request");
        htmlBody.Should().Contain("https://app.test/reset");
        htmlBody.Should().Contain("Acme");
        textBody.Should().Contain("https://app.test/reset");
        textBody.Should().Contain("Acme account");
    }

    [Fact]
    public void GetPasswordResetEmail_ShouldMentionOneHourExpiry()
    {
        var (_, htmlBody, textBody) = EmailTemplates.GetPasswordResetEmail("Alex", "https://app.test/reset");

        htmlBody.Should().Contain("expire in 1 hour");
        textBody.Should().Contain("expire in 1 hour");
    }

    [Fact]
    public void GetWelcomeEmail_ShouldUseCustomBrandingInSubjectAndBodies()
    {
        var (subject, htmlBody, textBody) = EmailTemplates.GetWelcomeEmail("Alex", CustomBranding);

        subject.Should().Be("Welcome to Acme!");
        htmlBody.Should().Contain("Hi Alex,");
        htmlBody.Should().Contain("using Acme");
        htmlBody.Should().Contain("#111111");
        textBody.Should().Contain("using Acme to manage your resources efficiently");
        textBody.Should().Contain("The Acme Team");
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
        htmlBody.Should().Contain("#667eea");
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
        textBody.Should().Contain("The Acme Team");
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
        h.Should().Contain("contact support");
        AssertBranded(s, h, t);
    }

    [Fact]
    public void TenantReactivated_hasOpenCta()
    {
        var (s, h, t) = EmailTemplates.GetTenantReactivatedEmail("Acme HQ", "https://app", CustomBranding);
        s.Should().Contain("active again");
        h.Should().Contain("https://app");
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
        h.Should().Contain("Dana").And.Contain("contact support");
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
        h1.Should().Contain("new@x.com").And.Contain("contact support");
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
}

using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class GeneralEmailTemplatesTests
{
    private const string DisplayName = "Bob";
    private const string Link = "https://app.example.com/verify?token=abc-123";

    // ─── GetVerificationEmail ─────────────────���──────────────────────────────

    [Fact]
    public void GetVerificationEmail_ReturnsCorrectSubject()
    {
        var (subject, _, _) = EmailTemplates.GetVerificationEmail(DisplayName, Link);

        subject.Should().Be("Verify your email address");
    }

    [Fact]
    public void GetVerificationEmail_ContainsDisplayNameAndLink()
    {
        var (_, html, text) = EmailTemplates.GetVerificationEmail(DisplayName, Link);

        html.Should().Contain(DisplayName);
        html.Should().Contain(Link);
        text.Should().Contain(DisplayName);
        text.Should().Contain(Link);
    }

    [Fact]
    public void GetVerificationEmail_WithCustomBranding_UsesBrandedProductName()
    {
        var branding = new EmailBranding("TestApp", "#ff0000", "#00ff00");

        var (_, html, text) = EmailTemplates.GetVerificationEmail(DisplayName, Link, branding);

        html.Should().Contain("TestApp");
        text.Should().Contain("TestApp");
        html.Should().Contain("#ff0000");
    }

    [Fact]
    public void GetVerificationEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetVerificationEmail(DisplayName, Link);

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── GetPasswordResetEmail ────────────────────────────────���──────────────

    [Fact]
    public void GetPasswordResetEmail_ReturnsCorrectSubject()
    {
        var (subject, _, _) = EmailTemplates.GetPasswordResetEmail(DisplayName, Link);

        subject.Should().Be("Reset your password");
    }

    [Fact]
    public void GetPasswordResetEmail_ContainsDisplayNameAndLink()
    {
        var (_, html, text) = EmailTemplates.GetPasswordResetEmail(DisplayName, Link);

        html.Should().Contain(DisplayName);
        html.Should().Contain(Link);
        text.Should().Contain(DisplayName);
        text.Should().Contain(Link);
    }

    [Fact]
    public void GetPasswordResetEmail_MentionsExpiry()
    {
        var (_, html, text) = EmailTemplates.GetPasswordResetEmail(DisplayName, Link);

        html.Should().Contain("expire in 1 hour");
        text.Should().Contain("expire in 1 hour");
    }

    [Fact]
    public void GetPasswordResetEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetPasswordResetEmail(DisplayName, Link);

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── GetWelcomeEmail ────────────────────────────���────────────────────────

    [Fact]
    public void GetWelcomeEmail_SubjectContainsWelcome()
    {
        var (subject, _, _) = EmailTemplates.GetWelcomeEmail(DisplayName);

        subject.Should().Contain("Welcome");
    }

    [Fact]
    public void GetWelcomeEmail_ContainsDisplayName()
    {
        var (_, html, text) = EmailTemplates.GetWelcomeEmail(DisplayName);

        html.Should().Contain(DisplayName);
        text.Should().Contain(DisplayName);
    }

    [Fact]
    public void GetWelcomeEmail_ContainsGettingStartedContent()
    {
        var (_, html, text) = EmailTemplates.GetWelcomeEmail(DisplayName);

        html.Should().Contain("Getting Started");
        text.Should().Contain("Getting Started");
    }

    [Fact]
    public void GetWelcomeEmail_WithCustomBranding_UsesBrandedProductName()
    {
        var branding = new EmailBranding("AcmeApp", "#123456", "#654321");

        var (subject, html, text) = EmailTemplates.GetWelcomeEmail(DisplayName, branding);

        subject.Should().Contain("AcmeApp");
        html.Should().Contain("AcmeApp");
        text.Should().Contain("AcmeApp");
    }

    [Fact]
    public void GetWelcomeEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetWelcomeEmail(DisplayName);

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── GetNewUserAlertEmail ───────────────────────────────���────────────────

    [Fact]
    public void GetNewUserAlertEmail_SubjectContainsUserEmail()
    {
        var (subject, _, _) = EmailTemplates.GetNewUserAlertEmail("alice@test.com", "Alice");

        subject.Should().Contain("alice@test.com");
    }

    [Fact]
    public void GetNewUserAlertEmail_BodyContainsUserDetails()
    {
        var (_, html, text) = EmailTemplates.GetNewUserAlertEmail("alice@test.com", "Alice");

        html.Should().Contain("alice@test.com");
        html.Should().Contain("Alice");
        text.Should().Contain("alice@test.com");
        text.Should().Contain("Alice");
    }

    [Fact]
    public void GetNewUserAlertEmail_ContainsTimestamp()
    {
        var (_, html, text) = EmailTemplates.GetNewUserAlertEmail("alice@test.com", "Alice");

        html.Should().Contain("UTC");
        text.Should().Contain("UTC");
    }

    [Fact]
    public void GetNewUserAlertEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetNewUserAlertEmail("alice@test.com", "Alice");

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── GetNewTenantAlertEmail ──────────────────────────────────────────────

    [Fact]
    public void GetNewTenantAlertEmail_SubjectContainsTenantSlug()
    {
        var (subject, _, _) = EmailTemplates.GetNewTenantAlertEmail("acme-corp", "Acme Corp", "owner@acme.com");

        subject.Should().Contain("acme-corp");
    }

    [Fact]
    public void GetNewTenantAlertEmail_BodyContainsTenantDetails()
    {
        var (_, html, text) = EmailTemplates.GetNewTenantAlertEmail("acme-corp", "Acme Corp", "owner@acme.com");

        html.Should().Contain("acme-corp");
        html.Should().Contain("Acme Corp");
        html.Should().Contain("owner@acme.com");
        text.Should().Contain("acme-corp");
        text.Should().Contain("Acme Corp");
        text.Should().Contain("owner@acme.com");
    }

    [Fact]
    public void GetNewTenantAlertEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetNewTenantAlertEmail("acme", "Acme", "owner@acme.com");

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── GetInvitationEmail ─────────────────────────��────────────────────────

    [Fact]
    public void GetInvitationEmail_SubjectContainsInvited()
    {
        var (subject, _, _) = EmailTemplates.GetInvitationEmail(Link, DateTime.UtcNow.AddDays(7));

        subject.Should().Contain("invited");
    }

    [Fact]
    public void GetInvitationEmail_ContainsSignupLink()
    {
        var (_, html, text) = EmailTemplates.GetInvitationEmail(Link, DateTime.UtcNow.AddDays(7));

        html.Should().Contain(Link);
        text.Should().Contain(Link);
    }

    [Fact]
    public void GetInvitationEmail_ContainsExpiryDate()
    {
        var expiresAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc);

        var (_, html, text) = EmailTemplates.GetInvitationEmail(Link, expiresAt);

        html.Should().Contain("May 01, 2026");
        text.Should().Contain("May 01, 2026");
    }

    [Fact]
    public void GetInvitationEmail_WithCustomBranding_UsesBrandedProductName()
    {
        var branding = new EmailBranding("MyPlatform", "#aaa", "#bbb");

        var (subject, html, text) = EmailTemplates.GetInvitationEmail(Link, DateTime.UtcNow.AddDays(7), branding);

        subject.Should().Contain("MyPlatform");
        html.Should().Contain("MyPlatform");
        text.Should().Contain("MyPlatform");
    }

    [Fact]
    public void GetInvitationEmail_ReturnsNonEmptyParts()
    {
        var (subject, html, text) = EmailTemplates.GetInvitationEmail(Link, DateTime.UtcNow.AddDays(7));

        subject.Should().NotBeNullOrWhiteSpace();
        html.Should().NotBeNullOrWhiteSpace();
        text.Should().NotBeNullOrWhiteSpace();
    }

    // ─── Default branding ─────────────────���──────────────────────────────���───

    [Fact]
    public void DefaultBranding_IsNotNull()
    {
        EmailBranding.Default.Should().NotBeNull();
        EmailBranding.Default.ProductName.Should().NotBeNullOrWhiteSpace();
        EmailBranding.Default.PrimaryColor.Should().NotBeNullOrWhiteSpace();
        EmailBranding.Default.SecondaryColor.Should().NotBeNullOrWhiteSpace();
    }
}

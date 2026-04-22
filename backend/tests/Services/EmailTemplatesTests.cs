using Api.Services;
using FluentAssertions;
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
}

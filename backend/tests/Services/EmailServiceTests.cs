using Api.Models;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Orkyo.Foundation.Tests.Services;

public class EmailServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly EmailService _emailService;

    public EmailServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();

        _mockConfiguration.Setup(c => c["SMTP_HOST"]).Returns("localhost");
        _mockConfiguration.Setup(c => c["SMTP_PORT"]).Returns("1025");
        _mockConfiguration.Setup(c => c["SMTP_USE_SSL"]).Returns("false");
        _mockConfiguration.Setup(c => c["SMTP_USERNAME"]).Returns("");
        _mockConfiguration.Setup(c => c["SMTP_PASSWORD"]).Returns("");
        _mockConfiguration.Setup(c => c["SMTP_FROM_EMAIL"]).Returns("test@example.com");
        _mockConfiguration.Setup(c => c["SMTP_FROM_NAME"]).Returns("Test");
        _mockConfiguration.Setup(c => c["APP_BASE_URL"]).Returns("http://localhost:5173");

        _emailService = new EmailService(_mockConfiguration.Object,
            new Mock<ILogger<EmailService>>().Object,
            CreateSettingsServiceMock());
    }

    private static ITenantSettingsService CreateSettingsServiceMock()
    {
        var mock = new Mock<ITenantSettingsService>();
        mock.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new TenantSettings());
        return mock.Object;
    }

    [Fact]
    public void SendVerificationEmailAsync_ShouldGenerateCorrectVerificationLink()
    {
        var method = typeof(EmailService).GetMethod("SendVerificationEmailAsync");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<bool>));
    }

    [Theory]
    [InlineData("abc123+def/ghi=", "abc123%2Bdef%2Fghi%3D")]
    [InlineData("simple-token", "simple-token")]
    [InlineData("test/token+with=special", "test%2Ftoken%2Bwith%3Dspecial")]
    public void VerificationToken_ShouldBeUrlEncoded(string rawToken, string expectedEncoded)
    {
        var encoded = Uri.EscapeDataString(rawToken);
        encoded.Should().Be(expectedEncoded);
    }

    [Fact]
    public void EmailService_ShouldImplementIEmailService()
    {
        _emailService.Should().BeAssignableTo<IEmailService>();
    }

    [Fact]
    public void SendLifecycleWarningEmailAsync_ShouldHaveCorrectSignature()
    {
        var method = typeof(EmailService).GetMethod("SendLifecycleWarningEmailAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<bool>));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(4);
        parameters[0].Name.Should().Be("toEmail");
        parameters[1].Name.Should().Be("displayName");
        parameters[2].Name.Should().Be("confirmToken");
        parameters[3].Name.Should().Be("warningNumber");
    }

    [Fact]
    public void SendDormancyNoticeEmailAsync_ShouldHaveCorrectSignature()
    {
        var method = typeof(EmailService).GetMethod("SendDormancyNoticeEmailAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<bool>));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(2);
        parameters[0].Name.Should().Be("toEmail");
        parameters[1].Name.Should().Be("displayName");
    }

    [Fact]
    public void IEmailService_ShouldDeclareLifecycleMethods()
    {
        var iface = typeof(IEmailService);
        iface.GetMethod("SendLifecycleWarningEmailAsync").Should().NotBeNull(
            "IEmailService must declare SendLifecycleWarningEmailAsync");
        iface.GetMethod("SendDormancyNoticeEmailAsync").Should().NotBeNull(
            "IEmailService must declare SendDormancyNoticeEmailAsync");
    }

    [Fact]
    public void IEmailService_ShouldDeclareAlertMethods()
    {
        var iface = typeof(IEmailService);
        iface.GetMethod("SendNewUserAlertAsync").Should().NotBeNull();
        iface.GetMethod("SendNewTenantAlertAsync").Should().NotBeNull();
    }

    [Fact]
    public async Task SendNewUserAlertAsync_WhenAdminEmailNotConfigured_ShouldReturnWithoutSending()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns((string?)null);

        var act = async () => await _emailService.SendNewUserAlertAsync("user@example.com", "Alice");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendNewTenantAlertAsync_WhenAdminEmailNotConfigured_ShouldReturnWithoutSending()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns((string?)null);

        var act = async () => await _emailService.SendNewTenantAlertAsync("my-slug", "My Tenant", "owner@example.com");

        await act.Should().NotThrowAsync();
    }

    private EmailService CreateUnreachableSmtpService()
    {
        _mockConfiguration.Setup(c => c["SMTP_HOST"]).Returns("localhost");
        _mockConfiguration.Setup(c => c["SMTP_PORT"]).Returns("19999");
        return new EmailService(_mockConfiguration.Object,
            new Mock<ILogger<EmailService>>().Object,
            CreateSettingsServiceMock());
    }

    [Fact]
    public async Task SendEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendEmailAsync("to@example.com", "User", "Subject", "<p>html</p>", "text");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendVerificationEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendVerificationEmailAsync("to@example.com", "User", "token123");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendPasswordResetEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendPasswordResetEmailAsync("to@example.com", "User", "reset-token");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendWelcomeEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendWelcomeEmailAsync("to@example.com", "User");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendInvitationEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendInvitationEmailAsync("to@example.com", "inv-token", DateTime.UtcNow.AddDays(7));
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendLifecycleWarningEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendLifecycleWarningEmailAsync("to@example.com", "User", "confirm-token", 1);
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendDormancyNoticeEmailAsync_WhenSmtpUnreachable_ShouldReturnFalse()
    {
        var service = CreateUnreachableSmtpService();
        var result = await service.SendDormancyNoticeEmailAsync("to@example.com", "User");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SendNewUserAlertAsync_WhenAdminEmailConfigured_ShouldNotThrow()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns("admin@example.com");
        var service = CreateUnreachableSmtpService();

        var act = async () => await service.SendNewUserAlertAsync("user@example.com", "Alice");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendNewTenantAlertAsync_WhenAdminEmailConfigured_ShouldNotThrow()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns("admin@example.com");
        var service = CreateUnreachableSmtpService();

        var act = async () => await service.SendNewTenantAlertAsync("my-slug", "My Tenant", "owner@example.com");
        await act.Should().NotThrowAsync();
    }
}

public class EmailTemplatesTests
{
    [Fact]
    public void GetVerificationEmail_ShouldReturnValidTemplate()
    {
        var displayName = "Test User";
        var verificationLink = "http://example.com/verify?token=abc123";

        var (subject, htmlBody, textBody) = EmailTemplates.GetVerificationEmail(displayName, verificationLink);

        subject.Should().NotBeNullOrEmpty();
        subject.ToLowerInvariant().Should().Contain("verify");
        htmlBody.Should().NotBeNullOrEmpty();
        htmlBody.Should().Contain(displayName);
        htmlBody.Should().Contain(verificationLink);
        htmlBody.Should().Contain("<!DOCTYPE html>");
        textBody.Should().NotBeNullOrEmpty();
        textBody.Should().Contain(displayName);
        textBody.Should().Contain(verificationLink);
    }

    [Fact]
    public void GetPasswordResetEmail_ShouldReturnValidTemplate()
    {
        var displayName = "Test User";
        var resetLink = "http://example.com/reset?token=xyz789";

        var (subject, htmlBody, textBody) = EmailTemplates.GetPasswordResetEmail(displayName, resetLink);

        subject.Should().NotBeNullOrEmpty();
        subject.ToLowerInvariant().Should().Contain("password");
        htmlBody.Should().NotBeNullOrEmpty();
        htmlBody.Should().Contain(displayName);
        htmlBody.Should().Contain(resetLink);
        htmlBody.Should().Contain("<!DOCTYPE html>");
        textBody.Should().NotBeNullOrEmpty();
        textBody.Should().Contain(displayName);
        textBody.Should().Contain(resetLink);
    }

    [Fact]
    public void GetWelcomeEmail_ShouldReturnValidTemplate()
    {
        var displayName = "Test User";

        var (subject, htmlBody, textBody) = EmailTemplates.GetWelcomeEmail(displayName);

        subject.Should().NotBeNullOrEmpty();
        subject.ToLowerInvariant().Should().Contain("welcome");
        htmlBody.Should().NotBeNullOrEmpty();
        htmlBody.Should().Contain(displayName);
        htmlBody.Should().Contain("<!DOCTYPE html>");
        textBody.Should().NotBeNullOrEmpty();
        textBody.Should().Contain(displayName);
    }

    [Fact]
    public void GetVerificationEmail_ShouldEscapeUserInput()
    {
        var displayName = "<script>alert('xss')</script>";
        var verificationLink = "http://example.com/verify?token=abc123";

        var (_, htmlBody, _) = EmailTemplates.GetVerificationEmail(displayName, verificationLink);

        htmlBody.Should().Contain(displayName);
    }

    [Fact]
    public void EmailTemplates_ShouldHaveBothHtmlAndTextVersions()
    {
        var displayName = "Test User";
        var link = "http://example.com/link";

        var verification = EmailTemplates.GetVerificationEmail(displayName, link);
        var passwordReset = EmailTemplates.GetPasswordResetEmail(displayName, link);
        var welcome = EmailTemplates.GetWelcomeEmail(displayName);

        verification.htmlBody.Should().NotBe(verification.textBody);
        passwordReset.htmlBody.Should().NotBe(passwordReset.textBody);
        welcome.htmlBody.Should().NotBe(welcome.textBody);
    }

    [Fact]
    public void GetNewUserAlertEmail_ShouldContainUserDetails()
    {
        var (subject, html, text) = EmailTemplates.GetNewUserAlertEmail("alice@example.com", "Alice");

        subject.Should().Contain("alice@example.com");
        html.Should().Contain("alice@example.com");
        html.Should().Contain("Alice");
        html.Should().Contain("<!DOCTYPE html>");
        text.Should().Contain("alice@example.com");
        text.Should().Contain("Alice");
    }

    [Fact]
    public void GetNewTenantAlertEmail_ShouldContainTenantDetails()
    {
        var (subject, html, text) = EmailTemplates.GetNewTenantAlertEmail("my-slug", "My Tenant", "owner@example.com");

        subject.Should().Contain("my-slug");
        html.Should().Contain("my-slug");
        html.Should().Contain("My Tenant");
        html.Should().Contain("owner@example.com");
        html.Should().Contain("<!DOCTYPE html>");
        text.Should().Contain("my-slug");
        text.Should().Contain("My Tenant");
        text.Should().Contain("owner@example.com");
    }

    [Fact]
    public void GetNewTenantAlertEmail_WithCustomBranding_UsesBrandedProductName()
    {
        var branding = new EmailBranding("Acme", "#000", "#fff");
        var (subject, html, text) = EmailTemplates.GetNewTenantAlertEmail("slug", "Name", "o@x.com", branding);

        subject.Should().Contain("Acme");
        html.Should().Contain("Acme");
        text.Should().Contain("Acme");
    }
}

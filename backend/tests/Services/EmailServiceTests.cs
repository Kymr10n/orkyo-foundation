using Api.Models;
using Api.Repositories;
using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Services;

public class EmailServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly InMemoryEmailOutbox _outbox = new();
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

        _emailService = CreateService(_outbox, CreateTransportMock(fails: false));
    }

    /// <summary>The production shape: the service queues into the outbox and the deliverer
    /// makes the one immediate attempt through the transport.</summary>
    private EmailService CreateService(IEmailOutboxRepository outbox, IEmailTransport transport) =>
        new(_mockConfiguration.Object,
            new Mock<ILogger<EmailService>>().Object,
            CreateSettingsServiceMock(),
            outbox,
            new EmailOutboxDeliverer(outbox, transport, NullLogger<EmailOutboxDeliverer>.Instance, TimeProvider.System));

    private static ITenantSettingsService CreateSettingsServiceMock()
    {
        var mock = new Mock<ITenantSettingsService>();
        mock.Setup(s => s.GetSettingsAsync()).ReturnsAsync(new TenantSettings());
        return mock.Object;
    }

    /// <summary>
    /// A failing transport stands in for the former unreachable-SMTP setup: EmailService sees the
    /// same thing either way, and the real connection failure now belongs to SmtpEmailTransportTests.
    /// </summary>
    private static IEmailTransport CreateTransportMock(bool fails)
    {
        var mock = new Mock<IEmailTransport>();
        var setup = mock.Setup(t => t.SendAsync(It.IsAny<MimeMessage>(), It.IsAny<CancellationToken>()));
        if (fails)
            setup.ThrowsAsync(new IOException("transport unavailable"));
        else
            setup.Returns(Task.CompletedTask);
        return mock.Object;
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
        parameters.Should().HaveCount(5); // toEmail, displayName, confirmToken, warningNumber, ct
        parameters[0].Name.Should().Be("toEmail");
        parameters[1].Name.Should().Be("displayName");
        parameters[2].Name.Should().Be("confirmToken");
        parameters[3].Name.Should().Be("warningNumber");
        parameters[4].Name.Should().Be("ct");
        parameters[4].ParameterType.Should().Be(typeof(System.Threading.CancellationToken));
    }

    [Fact]
    public void SendDormancyNoticeEmailAsync_ShouldHaveCorrectSignature()
    {
        var method = typeof(EmailService).GetMethod("SendDormancyNoticeEmailAsync");

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<bool>));

        var parameters = method.GetParameters();
        parameters.Should().HaveCount(3); // toEmail, displayName, ct
        parameters[0].Name.Should().Be("toEmail");
        parameters[1].Name.Should().Be("displayName");
        parameters[2].Name.Should().Be("ct");
        parameters[2].ParameterType.Should().Be(typeof(System.Threading.CancellationToken));
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

    private (EmailService Service, InMemoryEmailOutbox Outbox) CreateFailingTransportService()
    {
        var outbox = new InMemoryEmailOutbox();
        return (CreateService(outbox, CreateTransportMock(fails: true)), outbox);
    }

    [Fact]
    public async Task SendEmailAsync_WhenTransportSucceeds_QueuesTheMailAndMarksItSent()
    {
        var result = await _emailService.SendEmailAsync("to@example.com", "User", "Subject", "<p>html</p>", "text");

        result.Should().BeTrue();
        var row = _outbox.Single();
        row.Status.Should().Be("sent");
        row.Attempts.Should().Be(1);
        row.HtmlBody.Should().BeNull("bodies carry live tokens and are cleared on delivery");
        row.TextBody.Should().BeNull();
    }

    [Fact]
    public async Task SendEmailAsync_WhenTransportFails_QueuesTheMailAndLeavesItPendingForTheWorker()
    {
        var (service, outbox) = CreateFailingTransportService();

        var result = await service.SendEmailAsync("to@example.com", "User", "Subject", "<p>html</p>", "text");

        result.Should().BeTrue("the mail is durable in the outbox; delivery is the outbox's job");
        var row = outbox.Single();
        row.Status.Should().Be("pending");
        row.Attempts.Should().Be(1);
        row.LastError.Should().Contain("transport unavailable");
        row.NextAttemptAtUtc.Should().BeAfter(DateTime.UtcNow, "the retry is scheduled, not immediate");
        row.HtmlBody.Should().Be("<p>html</p>", "an undelivered mail keeps its body for the retry");
    }

    [Fact]
    public async Task SendEmailAsync_WhenTheOutboxWriteFails_ReturnsFalse()
    {
        var outbox = new Mock<IEmailOutboxRepository>();
        outbox.Setup(o => o.EnqueueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database unavailable"));
        var service = CreateService(outbox.Object, CreateTransportMock(fails: false));

        var result = await service.SendEmailAsync("to@example.com", "User", "Subject", "<p>html</p>", "text");

        result.Should().BeFalse("a mail that could not be queued is the one case the caller must treat as not sent");
    }

    [Fact]
    public async Task TemplatedSends_WhenTransportFails_AreQueuedNotLost()
    {
        var (service, outbox) = CreateFailingTransportService();

        (await service.SendWelcomeEmailAsync("to@example.com", "User")).Should().BeTrue();
        (await service.SendInvitationEmailAsync("to@example.com", "inv-token", DateTime.UtcNow.AddDays(7))).Should().BeTrue();
        (await service.SendLifecycleWarningEmailAsync("to@example.com", "User", "confirm-token", 1)).Should().BeTrue();
        (await service.SendDormancyNoticeEmailAsync("to@example.com", "User")).Should().BeTrue();

        outbox.Rows.Should().HaveCount(4).And.AllSatisfy(r => r.Status.Should().Be("pending"));
    }

    [Fact]
    public async Task SendNewUserAlertAsync_WhenAdminEmailConfigured_ShouldNotThrow()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns("admin@example.com");
        var (service, _) = CreateFailingTransportService();

        var act = async () => await service.SendNewUserAlertAsync("user@example.com", "Alice");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendNewTenantAlertAsync_WhenAdminEmailConfigured_ShouldNotThrow()
    {
        _mockConfiguration.Setup(c => c["ALERT_EMAIL_TO"]).Returns("admin@example.com");
        var (service, _) = CreateFailingTransportService();

        var act = async () => await service.SendNewTenantAlertAsync("my-slug", "My Tenant", "owner@example.com");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task NewLifecycleAndAdminSends_WhenTransportFails_AreQueuedForRetry()
    {
        // Exercises the template-build + queue path for every email added 2026-06 (covers the
        // EmailService delegations). Unreachable SMTP leaves each one pending in the outbox.
        var (s, outbox) = CreateFailingTransportService();
        (await s.SendTenantInactivityWarningAsync("a@x.com", "Acme", "https://app", 7)).Should().BeTrue();
        (await s.SendTenantSuspendedAsync("a@x.com", "Acme", "https://app", 90)).Should().BeTrue();
        (await s.SendTenantDeletingWarningAsync("a@x.com", "Acme", "https://app", 7)).Should().BeTrue();
        (await s.SendTenantDeletedAsync("a@x.com", "Acme")).Should().BeTrue();
        (await s.SendTenantWelcomeAsync("a@x.com", "Acme", "https://app")).Should().BeTrue();
        (await s.SendRoleChangedAsync("a@x.com", "Acme", "editor", "https://app")).Should().BeTrue();
        (await s.SendMemberRemovedAsync("a@x.com", "Acme")).Should().BeTrue();
        (await s.SendOwnershipReceivedAsync("a@x.com", "Acme", "https://app")).Should().BeTrue();
        (await s.SendOwnershipTransferredAsync("a@x.com", "Acme", "new@x.com")).Should().BeTrue();
        (await s.SendQuotaLimitReachedAsync("a@x.com", "Acme", "active seats", 25, "https://app")).Should().BeTrue();
        (await s.SendTierChangedAsync("a@x.com", "Acme", "professional", "https://app")).Should().BeTrue();
        (await s.SendPasswordChangedAsync("a@x.com", "Dana")).Should().BeTrue();
        (await s.SendMfaChangedAsync("a@x.com", "Dana", true)).Should().BeTrue();
        (await s.SendMfaChangedAsync("a@x.com", "Dana", false)).Should().BeTrue();
        (await s.SendPasskeyRemovedAsync("a@x.com", "Dana")).Should().BeTrue();
        (await s.SendEmailChangeRequestedOldAddressAsync("a@x.com", "Dana", "new@x.com")).Should().BeTrue();
        (await s.SendEmailChangedAsync("a@x.com", "Dana", "new@x.com")).Should().BeTrue();

        outbox.Rows.Should().HaveCount(17).And.AllSatisfy(r =>
        {
            r.Status.Should().Be("pending");
            r.Attempts.Should().Be(1);
        });
    }
}

public class WelcomeAndAlertEmailTemplateTests
{
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
    public void EmailTemplates_ShouldHaveBothHtmlAndTextVersions()
    {
        var welcome = EmailTemplates.GetWelcomeEmail("Test User");

        welcome.htmlBody.Should().NotBe(welcome.textBody);
    }

    [Fact]
    public void GetNewUserAlertEmail_ShouldContainUserDetails()
    {
        var (subject, html, text) = EmailTemplates.GetNewUserAlertEmail("alice@example.com", "Alice");

        subject.Should().Contain("alice@example.com");
        html.Should().Contain("alice@example.com");
        html.Should().Contain("Alice");
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

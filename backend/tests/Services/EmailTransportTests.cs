using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Orkyo.Foundation.Tests.Services;

public class SmtpEmailTransportTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["SMTP_HOST"] = "localhost",
            ["SMTP_PORT"] = "1025",
            ["SMTP_USE_SSL"] = "false",
            ["SMTP_FROM_EMAIL"] = "test@example.com",
            ["SMTP_FROM_NAME"] = "Test",
        };
        foreach (var (key, value) in overrides) values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static SmtpEmailTransport Create(IConfiguration configuration) =>
        new(configuration, new Mock<ILogger<SmtpEmailTransport>>().Object);

    private static MimeMessage Message()
    {
        var message = new MimeMessage();
        message.To.Add(new MailboxAddress("User", "to@example.com"));
        message.Subject = "Subject";
        message.Body = new TextPart("plain") { Text = "text" };
        return message;
    }

    [Fact]
    public async Task SendAsync_WhenSmtpUnreachable_Throws()
    {
        // The former EmailService unreachable-SMTP test: the real connection failure lives here
        // now, and EmailService turns it into `false` through its own retry loop.
        var transport = Create(Config(("SMTP_PORT", "19999")));

        var act = async () => await transport.SendAsync(Message());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SendAsync_SetsExactlyOneSender_AcrossRetries()
    {
        // EmailService retries with the same MimeMessage, so a transport that only appended
        // would put one more From on the message per attempt.
        var transport = Create(Config(("SMTP_PORT", "19999")));
        var message = Message();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { await transport.SendAsync(message); } catch (Exception) { /* connection refused */ }
        }

        message.From.Mailboxes.Should().ContainSingle()
            .Which.Address.Should().Be("test@example.com");
    }
}

public class LogOnlyEmailTransportTests
{
    private readonly Mock<ILogger<LogOnlyEmailTransport>> _logger = new();

    private MimeMessage Message()
    {
        var message = new MimeMessage();
        message.To.Add(new MailboxAddress("User", "to@example.com"));
        message.Subject = "Your invitation";
        message.Body = new TextPart("plain") { Text = "Open https://example.test/signup?invitation=tok" };
        return message;
    }

    [Fact]
    public async Task SendAsync_DoesNotThrow_AndDeliversNothing()
    {
        var transport = new LogOnlyEmailTransport(_logger.Object);

        var act = async () => await transport.SendAsync(Message());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_LogsRecipientSubjectAndBody()
    {
        // The operator recovers invitation links from the log, so all three must be in it.
        var transport = new LogOnlyEmailTransport(_logger.Object);

        await transport.SendAsync(Message());

        _logger.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("to@example.com")
                    && v.ToString()!.Contains("Your invitation")
                    && v.ToString()!.Contains("invitation=tok")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}

public class EmailTransportRegistrationTests
{
    private static IServiceCollection Services(string? smtpHost)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SMTP_HOST"] = smtpHost })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkyoEmailTransport(configuration);
        return services;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddOrkyoEmailTransport_WithoutSmtpHost_SelectsLogOnly(string? smtpHost)
    {
        // Empty counts as unset: the deploy pipeline writes KEY= for every unset key.
        using var provider = Services(smtpHost).BuildServiceProvider();

        provider.GetRequiredService<IEmailTransport>().Should().BeOfType<LogOnlyEmailTransport>();
        EmailTransportRegistration.IsLogOnly(provider.GetRequiredService<IConfiguration>()).Should().BeTrue();
    }

    [Fact]
    public void AddOrkyoEmailTransport_WithSmtpHost_SelectsSmtp()
    {
        using var provider = Services("smtp.example.com").BuildServiceProvider();

        provider.GetRequiredService<IEmailTransport>().Should().BeOfType<SmtpEmailTransport>();
        EmailTransportRegistration.IsLogOnly(provider.GetRequiredService<IConfiguration>()).Should().BeFalse();
    }
}

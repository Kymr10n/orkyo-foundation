using Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>Builds the one shape of message EmailService hands a transport: To, Subject, body, no From.</summary>
internal static class TransportTestMessage
{
    public static MimeMessage Create(string textBody = "text")
    {
        var message = new MimeMessage();
        message.To.Add(new MailboxAddress("User", "to@example.com"));
        message.Subject = "Your invitation";
        message.Body = new TextPart("plain") { Text = textBody };
        return message;
    }
}

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

    [Fact]
    public async Task SendAsync_WhenSmtpUnreachable_Throws()
    {
        // The former EmailService unreachable-SMTP test: the real connection failure lives here
        // now, and EmailService turns it into `false` through its own retry loop.
        var transport = Create(Config(("SMTP_PORT", "19999")));

        var act = async () => await transport.SendAsync(TransportTestMessage.Create());

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task SendAsync_SetsExactlyOneSender_AcrossRetries()
    {
        // EmailService retries with the same MimeMessage, so a transport that only appended
        // would put one more From on the message per attempt.
        var transport = Create(Config(("SMTP_PORT", "19999")));
        var message = TransportTestMessage.Create();

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

    [Fact]
    public async Task SendAsync_DoesNotThrow_AndDeliversNothing()
    {
        var transport = new LogOnlyEmailTransport(_logger.Object);

        var act = async () => await transport.SendAsync(TransportTestMessage.Create());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_LogsRecipientSubjectAndBody()
    {
        // The operator recovers invitation links from the log, so all three must be in it.
        var transport = new LogOnlyEmailTransport(_logger.Object);

        await transport.SendAsync(TransportTestMessage.Create("Open https://example.test/signup?invitation=tok"));

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
    private static ServiceProvider Build(string? smtpHost)
    {
        // A set host comes with the rest of the block, as the validator guarantees in a real
        // deployment: SmtpEmailTransport reads the block in its constructor, so resolving it
        // is also the construction guard the Turnstile test has.
        var values = new Dictionary<string, string?> { ["SMTP_HOST"] = smtpHost };
        if (!string.IsNullOrEmpty(smtpHost))
        {
            values["SMTP_PORT"] = "587";
            values["SMTP_USE_SSL"] = "true";
            values["SMTP_FROM_EMAIL"] = "noreply@example.com";
            values["SMTP_FROM_NAME"] = "Orkyo";
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddOrkyoEmailTransport(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddOrkyoEmailTransport_WithoutSmtpHost_SelectsLogOnly(string? smtpHost)
    {
        // Empty counts as unset: the deploy pipeline writes KEY= for every unset key.
        using var provider = Build(smtpHost);

        provider.GetRequiredService<IEmailTransport>().Should().BeOfType<LogOnlyEmailTransport>();
    }

    [Fact]
    public void AddOrkyoEmailTransport_WithSmtpHost_SelectsSmtp()
    {
        using var provider = Build("smtp.example.com");

        provider.GetRequiredService<IEmailTransport>().Should().BeOfType<SmtpEmailTransport>();
    }
}

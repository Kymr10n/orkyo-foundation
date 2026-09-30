using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared;

namespace Api.Services;

/// <summary>
/// Selects the email transport from configuration. Follows the key-gated shape of the Turnstile
/// provider: a set <c>SMTP_HOST</c> gives the real transport, an unset one gives the fallback.
///
/// Core stays free of any logging framework, so the warning is not raised here. Each host logs
/// <see cref="LogOnlyWarning"/> at its own registration site, where Serilog is available.
/// </summary>
public static class EmailTransportRegistration
{
    /// <summary>What a host logs at startup when mail is not delivered anywhere.</summary>
    public const string LogOnlyWarning = "Email delivery is log-only: SMTP_HOST is not set";

    /// <summary>
    /// True when no SMTP host is configured. Empty counts as unset: the deploy pipeline writes
    /// <c>KEY=</c> for every unset key.
    /// </summary>
    public static bool IsLogOnly(IConfiguration configuration) =>
        string.IsNullOrEmpty(configuration[ConfigKeys.SmtpHost]);

    /// <summary>
    /// Registers the transport that <see cref="EmailService"/> delivers through. Singleton in both
    /// hosts: neither implementation holds per-request state, and SmtpEmailTransport opens its
    /// connection per send.
    /// </summary>
    public static IServiceCollection AddOrkyoEmailTransport(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (IsLogOnly(configuration))
            services.AddSingleton<IEmailTransport, LogOnlyEmailTransport>();
        else
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        return services;
    }
}

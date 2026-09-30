using Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Services;

/// <summary>
/// Selects the email transport from configuration. Follows the key-gated shape of the Turnstile
/// provider: a set <c>SMTP_HOST</c> gives the real transport, an unset one gives the fallback.
/// The decision itself is <see cref="DeploymentConfig.IsSmtpConfigured"/>, shared with the
/// validator so the two can never disagree.
///
/// Core stays free of any logging framework, so the warning is not raised here. Each host logs
/// <see cref="LogOnlyWarning"/> at its own registration site, where Serilog is available.
/// </summary>
public static class EmailTransportRegistration
{
    /// <summary>What a host logs at startup when mail is not delivered anywhere.</summary>
    public const string LogOnlyWarning = "Email delivery is log-only: SMTP_HOST is not set";

    /// <summary>
    /// Registers the transport that <see cref="EmailService"/> delivers through. Singleton in both
    /// hosts: neither implementation holds per-request state, and SmtpEmailTransport opens its
    /// connection per send.
    /// </summary>
    public static IServiceCollection AddOrkyoEmailTransport(
        this IServiceCollection services, IConfiguration configuration)
    {
        if (DeploymentConfig.IsSmtpConfigured(configuration))
            services.AddSingleton<IEmailTransport, SmtpEmailTransport>();
        else
            services.AddSingleton<IEmailTransport, LogOnlyEmailTransport>();
        return services;
    }
}

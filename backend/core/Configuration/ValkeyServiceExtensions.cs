using Microsoft.Extensions.DependencyInjection;
using Orkyo.Shared;
using StackExchange.Redis;

namespace Api.Configuration;

/// <summary>
/// The Valkey (Redis-protocol) connection both editions register the same way: one
/// multiplexer for the process, from <see cref="ConfigKeys.ValkeyConnection"/>, and a
/// startup failure when the key is missing (no silent default). What each edition puts on top
/// — SaaS's Valkey-backed break-glass store, Community's null store — stays in its Program.cs
/// per the explicit-registration rule.
/// </summary>
public static class ValkeyServiceExtensions
{
    public static IServiceCollection AddOrkyoValkey(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetRequired(ConfigKeys.ValkeyConnection);
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        return services;
    }
}

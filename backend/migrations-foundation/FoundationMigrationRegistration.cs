using Microsoft.Extensions.DependencyInjection;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Migrations;

/// <summary>
/// DI extension that registers the foundation <see cref="IMigrationModule"/>.
/// Call after <c>AddOrkyoMigrationPlatform()</c> in the product migrator.
/// </summary>
public static class FoundationMigrationRegistration
{
    public static IServiceCollection AddFoundationMigrations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IMigrationModule, FoundationMigrationModule>();
        return services;
    }
}

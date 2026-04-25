using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Migrator;

/// <summary>
/// DI surface for foundation migration infrastructure. Product migrators (e.g.
/// <c>Orkyo.Saas.Migrator</c>) call <see cref="AddOrkyoMigrationPlatform"/> first,
/// then layer on <c>AddFoundationMigrations()</c>, <c>AddSaasMigrations()</c>, etc.
/// </summary>
public static class MigrationPlatformRegistration
{
    /// <summary>
    /// Registers the migration runner. Modules are picked up from DI — register them
    /// via the per-product <c>AddXxxMigrations()</c> extensions. Tenant iteration
    /// requires an <see cref="Orkyo.Migrations.Abstractions.ITenantRegistry"/> registration.
    /// </summary>
    public static IServiceCollection AddOrkyoMigrationPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<MigrationRunner>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Legacy adoption marks migrations a pre-migrator database had already run, so the runner
/// does not execute their SQL a second time against a live schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class LegacyAdoptionTests
{
    private readonly PostgresFixture _fixture;

    public LegacyAdoptionTests(PostgresFixture fixture) => _fixture = fixture;

    private static MigrationRunner BuildRunner()
    {
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddOrkyoMigrationPlatform()
            .AddFoundationMigrations()
            .BuildServiceProvider();
        return services.GetRequiredService<MigrationRunner>();
    }

    /// <summary>
    /// Regression guard. The baseline records what a legacy database ran BEFORE the migrator
    /// existed, so it carries ids from before scripts were renamed or dropped — the deployed
    /// file still lists 2010.saas.tenants, which ships today as 2010.saas.tenants_extensions.
    ///
    /// Rejecting those ids as typos broke every deploy at the adopt step: the release that
    /// did so failed staging with "Legacy-adoption baseline contains ids that match no known
    /// migration". Adopting a script that no longer exists is a no-op, so an unmatched id
    /// must be skipped, not fatal.
    /// </summary>
    [Fact]
    public async Task AdoptIds_WithIdsThatMatchNoShippedScript_SkipsThemInsteadOfThrowing()
    {
        var runner = BuildRunner();

        var options = new MigrationOptions
        {
            AdoptIds = new HashSet<string>
            {
                "2010.saas.tenants",             // renamed since; the deployed baseline still has it
                "2020.saas.tenant_memberships",  // no longer shipped at all
            },
        };

        var results = await runner.RunAsync(
            _fixture.ControlPlaneConnectionString,
            MigrationTargetDatabase.ControlPlane,
            lockKey: "legacy-adoption-unknown-ids",
            options);

        // The run completes; nothing was adopted for ids with no script behind them.
        Assert.NotNull(results);
        await using var conn = new NpgsqlConnection(_fixture.ControlPlaneConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM orkyo_schema_migrations WHERE id = ANY(@ids)", conn);
        cmd.Parameters.AddWithValue("ids", new[] { "2010.saas.tenants", "2020.saas.tenant_memberships" });
        Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
    }
}

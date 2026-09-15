using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// The environment rules the argv-only migrator entry point has always applied, now a value
/// object a product can build from its own configuration instead of mutating process
/// variables (the Community migrator did exactly that to alias its single database).
/// </summary>
public sealed class MigrationCliOptionsTests
{
    private static Func<string, string?> Env(params (string Key, string? Value)[] vars) =>
        key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void ReadsTheAspNetCoreConventionFirst()
    {
        var options = MigrationCliOptions.FromEnvironment(Env(
            (MigrationCliOptions.ConnectionStringEnvVar, "Host=cp"),
            (MigrationCliOptions.LegacyConnectionStringEnvVar, "Host=legacy")));

        options.ControlPlaneConnectionString.Should().Be("Host=cp");
        options.AppVersion.Should().BeNull();
        options.LockTimeoutSeconds.Should().Be(MigrationCliOptions.DefaultLockTimeoutSeconds);
    }

    [Fact]
    public void FallsBackToTheLegacyName()
    {
        var options = MigrationCliOptions.FromEnvironment(Env(
            (MigrationCliOptions.LegacyConnectionStringEnvVar, "Host=legacy"),
            (MigrationCliOptions.AppVersionEnvVar, "1.2.3"),
            (MigrationCliOptions.LockTimeoutEnvVar, "90")));

        options.ControlPlaneConnectionString.Should().Be("Host=legacy");
        options.AppVersion.Should().Be("1.2.3");
        options.LockTimeoutSeconds.Should().Be(90);
    }

    [Fact]
    public void RefusesToRun_WithoutAConnectionString()
    {
        var act = () => MigrationCliOptions.FromEnvironment(Env((MigrationCliOptions.ConnectionStringEnvVar, "")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__ControlPlane*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("soon")]
    public void RefusesANonPositiveOrNonNumericLockTimeout(string raw)
    {
        var act = () => MigrationCliOptions.FromEnvironment(Env(
            (MigrationCliOptions.ConnectionStringEnvVar, "Host=cp"),
            (MigrationCliOptions.LockTimeoutEnvVar, raw)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*MIGRATION_LOCK_TIMEOUT_SECONDS*");
    }

    [Fact]
    public void ExplicitOptions_CarryTheirOwnDefaults()
    {
        var options = new MigrationCliOptions("Host=single");

        options.AppVersion.Should().BeNull();
        options.LockTimeoutSeconds.Should().Be(60);
    }
}

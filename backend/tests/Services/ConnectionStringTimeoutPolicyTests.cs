using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class ConnectionStringTimeoutPolicyTests
{
    [Fact]
    public void ApplyDefaultCommandTimeout_WhenNoExplicitTimeout_PreservesNpgsqlDefault()
    {
        var input = "Host=localhost;Database=app;Username=postgres;Password=secret";

        var result = ConnectionStringTimeoutPolicy.ApplyDefaultCommandTimeout(input);

        // Npgsql's own default is 30, which equals the policy default. The
        // policy's clamping branch does not fire here.
        new NpgsqlConnectionStringBuilder(result).CommandTimeout
            .Should().Be(ConnectionStringTimeoutPolicy.DefaultCommandTimeoutSeconds);
    }

    [Fact]
    public void ApplyDefaultCommandTimeout_WhenExplicitNonZero_PreservesCallerValue()
    {
        var input = "Host=localhost;Database=app;Username=postgres;Password=secret;Command Timeout=77";

        var result = ConnectionStringTimeoutPolicy.ApplyDefaultCommandTimeout(input);

        new NpgsqlConnectionStringBuilder(result).CommandTimeout.Should().Be(77);
    }

    [Fact]
    public void ApplyDefaultCommandTimeout_WhenExplicitZeroInfinite_ClampsToDefault()
    {
        var input = "Host=localhost;Database=app;Username=postgres;Password=secret;Command Timeout=0";

        var result = ConnectionStringTimeoutPolicy.ApplyDefaultCommandTimeout(input);

        // Caller-supplied 0 (infinite) is clamped to the platform default
        // so a stuck query cannot hold a connection forever.
        new NpgsqlConnectionStringBuilder(result).CommandTimeout
            .Should().Be(ConnectionStringTimeoutPolicy.DefaultCommandTimeoutSeconds);
    }

    [Fact]
    public void DefaultCommandTimeoutSeconds_IsThirty()
    {
        ConnectionStringTimeoutPolicy.DefaultCommandTimeoutSeconds.Should().Be(30);
    }
}


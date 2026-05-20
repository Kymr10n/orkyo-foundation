using Orkyo.Foundation.Seed;
using Xunit;

namespace Orkyo.Foundation.Seed.Tests;

public class SafetyGuardTests
{
    // ── IsLocalLike ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("host.docker.internal")]
    [InlineData("my-machine.local")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.100")]
    public void IsLocalLike_ReturnsTrueForLocalAddresses(string host)
    {
        Assert.True(SafetyGuard.IsLocalLike(host));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("db.prod.example.com")]
    [InlineData("172.32.0.1")]   // just outside 172.16–31 range
    [InlineData("11.0.0.1")]     // not RFC1918
    public void IsLocalLike_ReturnsFalseForNonLocalAddresses(string host)
    {
        Assert.False(SafetyGuard.IsLocalLike(host));
    }

    [Fact]
    public void IsLocalLike_ReturnsFalseForEmptyHost()
    {
        Assert.False(SafetyGuard.IsLocalLike(""));
    }

    // ── AssertLocalOrForced ───────────────────────────────────────────────────

    [Fact]
    public void AssertLocalOrForced_DoesNotThrow_WhenForceNonLocalIsTrue()
    {
        using var conn = new Npgsql.NpgsqlConnection(
            "Host=db.prod.example.com;Database=d;Username=u;Password=p");
        var opts = new SeedOptions { Profile = "generic", Scale = "tiny", ForceNonLocal = true };
        SafetyGuard.AssertLocalOrForced(conn, opts);  // must not throw
    }

    [Fact]
    public void AssertLocalOrForced_DoesNotThrow_WhenEnvOverrideIsSet()
    {
        Environment.SetEnvironmentVariable(SafetyGuard.EnvOverride, "1");
        try
        {
            using var conn = new Npgsql.NpgsqlConnection(
                "Host=8.8.8.8;Database=d;Username=u;Password=p");
            var opts = new SeedOptions { Profile = "generic", Scale = "tiny" };
            SafetyGuard.AssertLocalOrForced(conn, opts);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SafetyGuard.EnvOverride, null);
        }
    }

    [Fact]
    public void AssertLocalOrForced_Throws_WhenNonLocalAndNoOverride()
    {
        Environment.SetEnvironmentVariable(SafetyGuard.EnvOverride, null);
        using var conn = new Npgsql.NpgsqlConnection(
            "Host=db.prod.example.com;Database=d;Username=u;Password=p");
        var opts = new SeedOptions { Profile = "generic", Scale = "tiny" };
        Assert.Throws<InvalidOperationException>(() =>
            SafetyGuard.AssertLocalOrForced(conn, opts));
    }

    [Fact]
    public void AssertLocalOrForced_DoesNotThrow_WhenLocalhost()
    {
        using var conn = new Npgsql.NpgsqlConnection(
            "Host=localhost;Database=d;Username=u;Password=p");
        var opts = new SeedOptions { Profile = "generic", Scale = "tiny" };
        SafetyGuard.AssertLocalOrForced(conn, opts);
    }
}

using System.Net;
using Npgsql;

namespace Orkyo.Foundation.Seed;

/// <summary>
/// Refuses to run the seeder against anything that doesn't look like a local
/// developer machine. The seeder issues TRUNCATE CASCADE and writes thousands of
/// rows — pointing it at a shared or production-looking database would be a
/// catastrophic data-loss event.
///
/// To bypass (e.g. CI fixture seeding into a containerised PG that resolves to
/// a non-local IP), either pass <c>--force-non-local</c> or set
/// <c>ORKYO_SEED_ALLOW=1</c> in the environment. Both are explicit opt-ins so
/// accidents stay accidents.
/// </summary>
public static class SafetyGuard
{
    public const string EnvOverride = "ORKYO_SEED_ALLOW";

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the connection points
    /// at a non-local host and no opt-in was provided.
    /// </summary>
    public static void AssertLocalOrForced(NpgsqlConnection conn, SeedOptions opts)
    {
        if (opts.ForceNonLocal) return;
        if (Environment.GetEnvironmentVariable(EnvOverride) == "1") return;

        var host = ExtractHost(conn.ConnectionString);
        if (IsLocalLike(host)) return;

        throw new InvalidOperationException(
            $"Refusing to seed: connection host '{host}' is not localhost. " +
            $"Pass --force-non-local or set {EnvOverride}=1 to override.");
    }

    internal static string ExtractHost(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return builder.Host ?? "";
    }

    internal static bool IsLocalLike(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;

        // Common dev names that always resolve to a developer's machine.
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

        // Numeric IPs: loopback + RFC1918 private + Docker default bridge.
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;

            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12 — covers Docker's default bridge (172.17.0.0/16) too
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
            }
        }

        return false;
    }
}

using System.Net;
using Npgsql;

namespace Orkyo.Foundation.Seed;

/// <summary>
/// Refuses to run the seeder against anything that doesn't look like a local
/// developer machine. The seeder issues TRUNCATE CASCADE and writes thousands of
/// rows — pointing it at a shared or production-looking database would be a
/// catastrophic data-loss event.
///
/// Only loopback and <c>host.docker.internal</c> count as local. To bypass (e.g. CI
/// fixture seeding into a containerised PG on a private IP), either pass <c>--force-non-local</c> or set
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

        // Loopback only. A private (RFC1918) address or a ".local" name can be a shared or
        // production server on the same network, and the seeder truncates with CASCADE.
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase)) return true;

        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }
}

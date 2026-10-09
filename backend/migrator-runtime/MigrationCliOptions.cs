namespace Orkyo.Migrator;

/// <summary>
/// What <see cref="MigrationCli"/> needs beyond argv. Products that run one database (Community)
/// build this from their own configuration; <see cref="FromEnvironment()"/> is the historical
/// behaviour — the ASP.NET Core env-var convention with the legacy name as a fallback — and what
/// the argv-only overload of <c>RunMigrationCliAsync</c> uses.
/// </summary>
/// <param name="ControlPlaneConnectionString">The control-plane database (Community: its only database).</param>
/// <param name="AppVersion">Recorded as <c>applied_by_version</c>; null when unknown.</param>
/// <param name="LockTimeoutSeconds">Advisory-lock acquisition timeout.</param>
/// <param name="TenantConcurrency">
/// How many tenant databases migrate at once. Each tenant takes its own advisory lock and its
/// own connections, so the runs are independent; the bound keeps the migrator's connection
/// use inside the budget (<c>docs/40-compose-stacks.md</c> in orkyo-infra). 1 is the
/// sequential behaviour the migrator had before 1.5.0.
/// </param>
public sealed record MigrationCliOptions(
    string ControlPlaneConnectionString,
    string? AppVersion = null,
    int LockTimeoutSeconds = MigrationCliOptions.DefaultLockTimeoutSeconds,
    int TenantConcurrency = MigrationCliOptions.DefaultTenantConcurrency)
{
    public const string ConnectionStringEnvVar = "ConnectionStrings__ControlPlane";
    public const string LegacyConnectionStringEnvVar = "CONTROL_PLANE_CONNECTION_STRING";
    public const string AppVersionEnvVar = "APP_VERSION";
    public const string LockTimeoutEnvVar = "MIGRATION_LOCK_TIMEOUT_SECONDS";
    public const string TenantConcurrencyEnvVar = "MIGRATION_TENANT_CONCURRENCY";
    public const int DefaultLockTimeoutSeconds = 60;
    public const int DefaultTenantConcurrency = 4;

    public static MigrationCliOptions FromEnvironment() => FromEnvironment(Environment.GetEnvironmentVariable);

    /// <summary>The same rules over any variable source, so they can be tested without touching the process.</summary>
    public static MigrationCliOptions FromEnvironment(Func<string, string?> read)
    {
        ArgumentNullException.ThrowIfNull(read);

        var connectionString = read(ConnectionStringEnvVar);
        if (string.IsNullOrEmpty(connectionString)) connectionString = read(LegacyConnectionStringEnvVar);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException(
                $"Neither {ConnectionStringEnvVar} nor {LegacyConnectionStringEnvVar} is set. " +
                "The migrator needs a connection to the control-plane database to run any command.");
        }

        var appVersion = read(AppVersionEnvVar);
        var lockTimeout = PositiveInt(read, LockTimeoutEnvVar, DefaultLockTimeoutSeconds);
        var tenantConcurrency = PositiveInt(read, TenantConcurrencyEnvVar, DefaultTenantConcurrency);
        return new MigrationCliOptions(
            connectionString, string.IsNullOrEmpty(appVersion) ? null : appVersion, lockTimeout, tenantConcurrency);
    }

    private static int PositiveInt(Func<string, string?> read, string name, int fallback)
    {
        var raw = read(name);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!int.TryParse(raw, out var value) || value <= 0)
        {
            throw new InvalidOperationException($"{name} must be a positive integer (got '{raw}').");
        }
        return value;
    }
}

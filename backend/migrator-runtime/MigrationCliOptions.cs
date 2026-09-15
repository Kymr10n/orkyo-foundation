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
public sealed record MigrationCliOptions(
    string ControlPlaneConnectionString,
    string? AppVersion = null,
    int LockTimeoutSeconds = MigrationCliOptions.DefaultLockTimeoutSeconds)
{
    public const string ConnectionStringEnvVar = "ConnectionStrings__ControlPlane";
    public const string LegacyConnectionStringEnvVar = "CONTROL_PLANE_CONNECTION_STRING";
    public const string AppVersionEnvVar = "APP_VERSION";
    public const string LockTimeoutEnvVar = "MIGRATION_LOCK_TIMEOUT_SECONDS";
    public const int DefaultLockTimeoutSeconds = 60;

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

        var rawTimeout = read(LockTimeoutEnvVar);
        var lockTimeout = DefaultLockTimeoutSeconds;
        if (!string.IsNullOrWhiteSpace(rawTimeout))
        {
            if (!int.TryParse(rawTimeout, out lockTimeout) || lockTimeout <= 0)
            {
                throw new InvalidOperationException(
                    $"{LockTimeoutEnvVar} must be a positive integer (got '{rawTimeout}').");
            }
        }

        return new MigrationCliOptions(connectionString, string.IsNullOrEmpty(appVersion) ? null : appVersion, lockTimeout);
    }
}

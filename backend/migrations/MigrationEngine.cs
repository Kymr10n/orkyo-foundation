using System.Text.RegularExpressions;
using Npgsql;

namespace Orkyo.Migrations;

/// <summary>
/// Core migration engine shared by the standalone Orkyo.Migrator, the API's
/// DatabaseMigrationService, and TenantProvisioningService.
/// All migration logic lives here — callers are thin orchestration wrappers.
/// </summary>
public static partial class MigrationEngine
{
    /// <summary>
    /// Advisory lock ID used to prevent concurrent migration runs.
    /// Same across all environments — standalone migrator, API dev-mode, and provisioning.
    /// </summary>
    public const long AdvisoryLockId = 7_300_100; // "orkyo-migrate"

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10)
    ];

    [GeneratedRegex(@"^[a-z0-9_]+$")]
    private static partial Regex SafeIdentifierPattern();

    // ─── Public API ────────────────────────────────────────────────────

    /// <summary>
    /// Applies pending SQL migrations to a single database within a transaction.
    /// Idempotent — safe to call repeatedly.
    /// </summary>
    public static async Task<int> ApplyMigrationsAsync(
        string connectionString,
        string migrationsDirectory,
        string databaseName,
        Action<string>? logger = null)
    {
        logger?.Invoke($"  Applying migrations to {databaseName}...");

        if (!Directory.Exists(migrationsDirectory))
        {
            logger?.Invoke($"  Migrations directory not found: {migrationsDirectory}");
            return 0;
        }

        var migrationFiles = Directory.GetFiles(migrationsDirectory, "*.sql")
            .OrderBy(f => f)
            .ToList();

        if (migrationFiles.Count == 0)
        {
            logger?.Invoke($"  No migrations found for {databaseName}");
            return 0;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        // Create tracking table if it doesn't exist
        await using (var cmd = new NpgsqlCommand(@"
            CREATE TABLE IF NOT EXISTS _migrations (
                filename TEXT PRIMARY KEY,
                applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            )", connection))
        {
            await cmd.ExecuteNonQueryAsync();
        }

        // Get already-applied migrations
        var appliedMigrations = new HashSet<string>();
        await using (var cmd = new NpgsqlCommand("SELECT filename FROM _migrations", connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                appliedMigrations.Add(reader.GetString(0));
        }

        // Apply pending migrations
        var appliedCount = 0;
        foreach (var migrationFile in migrationFiles)
        {
            var filename = Path.GetFileName(migrationFile);
            if (appliedMigrations.Contains(filename))
                continue;

            logger?.Invoke($"    Applying {filename}...");
            var sql = await File.ReadAllTextAsync(migrationFile);

            await using var tx = await connection.BeginTransactionAsync();
            try
            {
                await using (var cmd = new NpgsqlCommand(sql, connection, tx))
                    await cmd.ExecuteNonQueryAsync();

                await using (var cmd = new NpgsqlCommand(
                    "INSERT INTO _migrations (filename) VALUES ($1)", connection, tx))
                {
                    cmd.Parameters.AddWithValue(filename);
                    await cmd.ExecuteNonQueryAsync();
                }

                await tx.CommitAsync();
                appliedCount++;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                throw new InvalidOperationException(
                    $"Migration {filename} failed on {databaseName}: {ex.Message}", ex);
            }
        }

        if (appliedCount > 0)
            logger?.Invoke($"  Applied {appliedCount} migration(s) to {databaseName}");
        else
            logger?.Invoke($"  {databaseName} is up-to-date");

        return appliedCount;
    }

    /// <summary>
    /// Ensures a database exists, creating it if necessary.
    /// Validates the identifier to prevent SQL injection.
    /// </summary>
    public static async Task EnsureDatabaseExists(
        string adminConnectionString,
        string database,
        Action<string>? logger = null)
    {
        ValidateDatabaseIdentifier(database);

        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync();

        await using var checkCmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @dbname", conn);
        checkCmd.Parameters.AddWithValue("dbname", database);
        var exists = await checkCmd.ExecuteScalarAsync() != null;

        if (!exists)
        {
            logger?.Invoke($"  Creating database {database}...");
            // Identifier is validated above — safe to interpolate for DDL
            await using var createCmd = new NpgsqlCommand(
                $"CREATE DATABASE \"{database}\"", conn);
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Discovers all tenant database identifiers from the control_plane database.
    /// Returns an empty list if the tenants table doesn't exist yet (first boot).
    /// </summary>
    public static async Task<List<string>> DiscoverTenantDatabasesAsync(
        string controlPlaneConnectionString,
        Action<string>? logger = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(controlPlaneConnectionString);
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(
                "SELECT db_identifier FROM tenants WHERE status != 'deleting'", conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            var databases = new List<string>();
            while (await reader.ReadAsync())
                databases.Add(reader.GetString(0));

            return databases;
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table
        {
            logger?.Invoke("  Tenants table not found (first boot) — skipping tenant discovery");
            return [];
        }
    }

    /// <summary>
    /// Acquires a PostgreSQL advisory lock, runs the migration action, then releases.
    /// Blocks until the lock is available — prevents concurrent migration runs.
    /// </summary>
    public static async Task WithAdvisoryLockAsync(
        string adminConnectionString,
        Func<Task> action,
        Action<string>? logger = null)
    {
        await using var lockConnection = new NpgsqlConnection(adminConnectionString);
        await lockConnection.OpenAsync();

        logger?.Invoke($"Acquiring advisory lock (id={AdvisoryLockId})...");
        await using (var lockCmd = new NpgsqlCommand(
            $"SELECT pg_advisory_lock({AdvisoryLockId})", lockConnection))
        {
            await lockCmd.ExecuteNonQueryAsync();
        }
        logger?.Invoke("Advisory lock acquired.");

        try
        {
            await action();
        }
        finally
        {
            logger?.Invoke("Releasing advisory lock...");
            await using var unlockCmd = new NpgsqlCommand(
                $"SELECT pg_advisory_unlock({AdvisoryLockId})", lockConnection);
            await unlockCmd.ExecuteNonQueryAsync();
            logger?.Invoke("Advisory lock released.");
        }
    }

    /// <summary>
    /// Migrates a single database (ensure exists + apply SQL files) with retry.
    /// Builds the target connection string from <paramref name="adminConnectionString"/>
    /// by swapping the database name — no need to pass credentials separately.
    /// </summary>
    public static async Task<int> MigrateDatabaseAsync(
        string adminConnectionString,
        string database, string migrationsDirectory,
        Action<string>? logger = null)
    {
        return await WithRetryAsync(async () =>
        {
            await EnsureDatabaseExists(adminConnectionString, database, logger);
            var dbCs = BuildConnectionString(adminConnectionString, database);
            return await ApplyMigrationsAsync(dbCs, migrationsDirectory, database, logger);
        }, database, logger);
    }

    /// <summary>
    /// Finds the migrations root directory.
    /// Docker: /infra/db/migrations (mounted/copied into image).
    /// Local: walks up from startDirectory until infra/db/migrations is found.
    /// </summary>
    public static string FindMigrationsRoot(string? startDirectory = null)
    {
        const string dockerPath = "/infra/db/migrations";
        if (Directory.Exists(dockerPath))
            return dockerPath;

        var dir = startDirectory ?? Directory.GetCurrentDirectory();
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "infra", "db", "migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not find migrations directory (infra/db/migrations). " +
            "Checked /infra/db/migrations (Docker) and searched upward from current directory.");
    }

    /// <summary>
    /// Builds a connection string targeting the 'postgres' admin database,
    /// preserving all other fields (host, port, credentials) from <paramref name="connectionString"/>.
    /// </summary>
    public static string BuildAdminConnectionString(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString;

    /// <summary>
    /// Builds a connection string targeting <paramref name="database"/>,
    /// preserving all other fields (host, port, credentials) from <paramref name="connectionString"/>.
    /// </summary>
    public static string BuildConnectionString(string connectionString, string database)
        => new NpgsqlConnectionStringBuilder(connectionString) { Database = database }.ConnectionString;

    // ─── Internals ─────────────────────────────────────────────────────

    private static void ValidateDatabaseIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("Database identifier cannot be empty.", nameof(identifier));

        if (!SafeIdentifierPattern().IsMatch(identifier))
            throw new ArgumentException(
                $"Database identifier '{identifier}' contains unsafe characters. " +
                "Only lowercase letters, digits, and underscores are allowed.", nameof(identifier));
    }

    private static async Task<int> WithRetryAsync(
        Func<Task<int>> action,
        string databaseName,
        Action<string>? logger)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (NpgsqlException ex) when (attempt < MaxRetries)
            {
                var delay = RetryDelays[attempt];
                logger?.Invoke(
                    $"  Transient error migrating {databaseName} (attempt {attempt + 1}/{MaxRetries + 1}): " +
                    $"{ex.Message}. Retrying in {delay.TotalSeconds}s...");
                await Task.Delay(delay);
            }
        }
    }
}

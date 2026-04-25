using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// Composition-agnostic migration runner. Given a connection string, a target database,
/// and a set of <see cref="IMigrationModule"/> registrations, applies (or validates)
/// the contributed migrations against a single Postgres database.
///
/// DbUp executes the SQL and writes journal rows; the Orkyo runner is the only caller
/// of DbUp and adds:
/// <list type="bullet">
///   <item>Module composition + deterministic ordering across modules</item>
///   <item>Postgres advisory locks for cross-process mutual exclusion</item>
///   <item>Pre-flight checksum validation against already-applied rows (immutability)</item>
///   <item>An augmented <c>orkyo_schema_migrations</c> journal (module, target, checksum, version)</item>
/// </list>
/// Multi-database orchestration (control-plane + per-tenant) is the caller's responsibility:
/// invoke <see cref="RunAsync"/> once per database with the appropriate connection + lock key.
/// </summary>
public sealed class MigrationRunner
{
    private readonly IReadOnlyList<IMigrationModule> _modules;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(
        IEnumerable<IMigrationModule> modules,
        ILogger<MigrationRunner>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(modules);
        _modules = modules.ToList();
        _logger = logger ?? NullLogger<MigrationRunner>.Instance;
    }

    public async Task<IReadOnlyList<MigrationResult>> RunAsync(
        string connectionString,
        MigrationTargetDatabase target,
        string lockKey,
        MigrationOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);
        options ??= new MigrationOptions();

        if (options.TargetFilter is { } filter && filter != target)
        {
            _logger.LogInformation(
                "MigrationRunner: skipping target {Target} because options.TargetFilter={Filter}",
                target, filter);
            return Array.Empty<MigrationResult>();
        }

        var ordered = MigrationOrderer.Order(_modules, target);
        if (ordered.Count == 0)
        {
            _logger.LogInformation("MigrationRunner: no migrations registered for target {Target}", target);
            return Array.Empty<MigrationResult>();
        }

        // Hold the advisory lock on a dedicated session for the entire run. DbUp will
        // open its own connections for script execution; mutual exclusion across
        // migrator processes is what the lock provides, not transactional coupling.
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);
        await using var lease = await AdvisoryLock.AcquireAsync(
            lockConnection, lockKey, TimeSpan.FromSeconds(options.LockTimeoutSeconds), ct);

        // Pre-flight: ensure history table exists, load applied state, validate checksums.
        await using var historyConnection = new NpgsqlConnection(connectionString);
        await historyConnection.OpenAsync(ct);
        var history = new MigrationHistory(historyConnection);
        await history.EnsureTableExistsAsync(ct);
        var applied = await history.LoadAppliedAsync(ct);

        ValidateAppliedChecksums(ordered, applied);

        if (options.Mode == MigrationExecutionMode.ValidateOnly)
        {
            return BuildValidateOnlyResults(ordered, applied);
        }

        if (options.Mode == MigrationExecutionMode.DryRun)
        {
            throw new NotSupportedException(
                "DryRun mode is deferred under the slim DbUp spec. Use ValidateOnly to verify " +
                "ordering / checksum drift, and rely on the disposable-PG CI job for full apply " +
                "validation. See requirements/orkyo-dbup-migration-spec-for-copilot.md.");
        }

        // Apply mode: hand pending scripts to DbUp.
        var pending = ordered.Where(s => !applied.ContainsKey(s.Id)).ToList();
        if (pending.Count == 0)
        {
            _logger.LogInformation(
                "MigrationRunner: no pending migrations for target {Target} ({Count} already applied)",
                target, ordered.Count);
            return ordered.Select(s => new MigrationResult(s, MigrationOutcome.Skipped, null, null)).ToList();
        }

        _logger.LogInformation(
            "MigrationRunner: applying {Pending} pending migration(s) to target {Target} via DbUp",
            pending.Count, target);

        var dbUpScripts = pending
            .Select(s => new SqlScript(s.Id, s.Sql))
            .ToArray();

        var byId = ordered.ToDictionary(s => s.Id, StringComparer.Ordinal);

        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScripts(dbUpScripts)
            .JournalTo((connectionManager, log) =>
                new OrkyoDbUpJournal(connectionManager, log, byId, options.AppliedByVersion))
            .LogTo(new DbUpLoggerAdapter(_logger))
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            var failingId = result.ErrorScript?.Name ?? "(unknown)";
            var failingModule = byId.TryGetValue(failingId, out var s) ? s.Module : "(unknown)";
            throw new InvalidOperationException(
                $"Migration failed at '{failingId}' (module '{failingModule}', target '{target}'): " +
                $"{result.Error?.Message ?? "DbUp reported failure with no inner exception"}",
                result.Error);
        }

        var appliedIds = result.Scripts.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        return ordered.Select(s =>
            appliedIds.Contains(s.Id)
                ? new MigrationResult(s, MigrationOutcome.Applied, null, null)
                : new MigrationResult(s, MigrationOutcome.Skipped, null, null))
            .ToList();
    }

    private static void ValidateAppliedChecksums(
        IReadOnlyList<MigrationScript> ordered,
        IReadOnlyDictionary<string, AppliedMigration> applied)
    {
        foreach (var script in ordered)
        {
            if (!applied.TryGetValue(script.Id, out var existing)) continue;
            if (string.Equals(existing.Checksum, script.Checksum, StringComparison.Ordinal)) continue;

            throw new InvalidOperationException(
                $"Checksum drift detected for already-applied migration '{script.Id}' " +
                $"(module '{existing.Module}'). " +
                $"History has '{existing.Checksum}', code has '{script.Checksum}'. " +
                $"Applied migrations are immutable — author a new migration instead of editing this one.");
        }
    }

    private static IReadOnlyList<MigrationResult> BuildValidateOnlyResults(
        IReadOnlyList<MigrationScript> ordered,
        IReadOnlyDictionary<string, AppliedMigration> applied)
    {
        return ordered.Select(s =>
            applied.ContainsKey(s.Id)
                ? new MigrationResult(s, MigrationOutcome.Validated, null, null)
                : new MigrationResult(s, MigrationOutcome.Failed, null,
                    $"ValidateOnly: migration '{s.Id}' is pending in code but not in history."))
            .ToList();
    }
}

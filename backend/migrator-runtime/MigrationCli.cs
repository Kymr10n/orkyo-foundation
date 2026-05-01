using DbUp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Migrator;

/// <summary>
/// CLI entry point for product migrators. Parses argv, resolves
/// <see cref="MigrationRunner"/> + <see cref="ITenantRegistry"/> from DI, and orchestrates
/// the control-plane → tenant flow described in the migration spec.
/// </summary>
/// <remarks>
/// Supported commands:
/// <code>
///   migrate  --target all|control-plane|tenant [--tenant-slug X] [--tenant-id Y]
///   validate --target all|control-plane|tenant [--tenant-slug X] [--tenant-id Y]
/// </code>
/// Required env: <c>CONTROL_PLANE_CONNECTION_STRING</c>.
/// Optional env: <c>APP_VERSION</c>, <c>MIGRATION_LOCK_TIMEOUT_SECONDS</c>.
/// </remarks>
public static class MigrationCli
{
    private const string ControlPlaneLockKey = "orkyo:control-plane";

    public static async Task<int> RunMigrationCliAsync(
        this IServiceProvider services,
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        var loggerFactory = services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
        var logger = loggerFactory.CreateLogger("Orkyo.Migrator.Cli");

        try
        {
            var parsed = CliArgs.Parse(args);

            // Read using ASP.NET Core's standard env-var convention (ConnectionStrings:ControlPlane →
            // ConnectionStrings__ControlPlane). Fall back to the legacy CONTROL_PLANE_CONNECTION_STRING
            // for docker-compose configs that haven't been updated yet.
            var connectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__ControlPlane")
                ?? Environment.GetEnvironmentVariable("CONTROL_PLANE_CONNECTION_STRING")
                ?? throw new InvalidOperationException(
                    "Neither ConnectionStrings__ControlPlane nor CONTROL_PLANE_CONNECTION_STRING is set. " +
                    "The migrator needs a connection to the control-plane database to run any command.");

            var appVersion = Environment.GetEnvironmentVariable("APP_VERSION");
            var lockTimeout = ParseLockTimeoutSeconds();

            LegacyAdoptionBaseline? baseline = null;
            if (parsed.AdoptLegacyPath is { } adoptPath)
            {
                baseline = LegacyAdoptionBaseline.LoadFromFile(adoptPath);
                logger.LogWarning(
                    "Legacy adoption ENABLED: marking {Cp} control-plane + {Tn} tenant migration ids as already-applied " +
                    "before this run. Source: {Path}",
                    baseline.ControlPlaneIds.Count, baseline.TenantIds.Count, adoptPath);
            }

            var baseOptions = new MigrationOptions
            {
                Mode = parsed.Command == CliCommand.Validate
                    ? MigrationExecutionMode.ValidateOnly
                    : MigrationExecutionMode.Apply,
                AppliedByVersion = baseline?.AppliedByVersion ?? appVersion,
                LockTimeoutSeconds = lockTimeout,
            };

            var runner = services.GetRequiredService<MigrationRunner>();

            // Control-plane phase
            if (parsed.Target is CliTarget.All or CliTarget.ControlPlane)
            {
                var cpOptions = baseline is null
                    ? baseOptions
                    : baseOptions with { AdoptIds = baseline.ControlPlaneIds };
                logger.LogInformation("== Control-plane migrations ({Mode}) ==", cpOptions.Mode);
                var cpResults = await runner.RunAsync(
                    connectionString, MigrationTargetDatabase.ControlPlane, ControlPlaneLockKey, cpOptions, cancellationToken);
                ReportRun(logger, "control-plane", cpResults);
            }

            // Tenant phase
            if (parsed.Target is CliTarget.All or CliTarget.Tenant)
            {
                var registry = services.GetService<ITenantRegistry>()
                    ?? throw new InvalidOperationException(
                        "No ITenantRegistry registered. Tenant-target migrations require the product " +
                        "migrator to register an ITenantRegistry implementation (typically in AddSaasMigrations()).");

                var tenants = await registry.ListActiveTenantsAsync(connectionString, cancellationToken);
                tenants = ApplyTenantFilter(tenants, parsed);

                var tenantOptions = baseline is null
                    ? baseOptions
                    : baseOptions with { AdoptIds = baseline.TenantIds };

                logger.LogInformation("== Tenant migrations ({Mode}): {Count} tenant(s) ==",
                    tenantOptions.Mode, tenants.Count);

                foreach (var tenant in tenants)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    logger.LogInformation("-- tenant {Slug} ({Id}) --", tenant.Slug, tenant.Id);

                    // Tenant databases may not yet exist (new tenants provisioned via a seed
                    // migration, CI environments, first-run deployments). Create the database
                    // if absent — this is a no-op for already-provisioned tenants.
                    EnsureDatabase.For.PostgresqlDatabase(tenant.ConnectionString);

                    var tResults = await runner.RunAsync(
                        tenant.ConnectionString,
                        MigrationTargetDatabase.Tenant,
                        $"orkyo:tenant:{tenant.Id}",
                        tenantOptions,
                        cancellationToken);
                    ReportRun(logger, $"tenant:{tenant.Slug}", tResults);
                }
            }

            // ValidateOnly: surface failures via exit code even though no exception was thrown.
            if (baseOptions.Mode == MigrationExecutionMode.ValidateOnly)
            {
                // The runner returned Failed entries for pending-but-not-applied scripts.
                // Treat any Failed in the aggregate as a validation failure → non-zero exit.
                // (We don't aggregate across calls here; per-call ReportRun logs the failures.)
                logger.LogInformation("Validate completed. Inspect logs for any 'pending' rows.");
            }
            else
            {
                logger.LogInformation("All migrations applied successfully.");
            }
            return 0;
        }
        catch (CliUsageException ex)
        {
            logger.LogError("{Message}\n\n{Usage}", ex.Message, CliUsage.Text);
            return 2;
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Migration cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Migration failed: {Message}", ex.Message);
            return 1;
        }
    }

    private static int ParseLockTimeoutSeconds()
    {
        var raw = Environment.GetEnvironmentVariable("MIGRATION_LOCK_TIMEOUT_SECONDS");
        if (string.IsNullOrWhiteSpace(raw)) return 60;
        if (!int.TryParse(raw, out var v) || v <= 0)
        {
            throw new InvalidOperationException(
                $"MIGRATION_LOCK_TIMEOUT_SECONDS must be a positive integer (got '{raw}').");
        }
        return v;
    }

    private static IReadOnlyList<TenantDatabase> ApplyTenantFilter(
        IReadOnlyList<TenantDatabase> tenants,
        CliArgs parsed)
    {
        if (parsed.TenantSlug is { } slug)
        {
            return tenants.Where(t => string.Equals(t.Slug, slug, StringComparison.Ordinal)).ToList();
        }
        if (parsed.TenantId is { } id)
        {
            return tenants.Where(t => string.Equals(t.Id, id, StringComparison.Ordinal)).ToList();
        }
        return tenants;
    }

    private static void ReportRun(ILogger logger, string label, IReadOnlyList<MigrationResult> results)
    {
        var applied = results.Count(r => r.Outcome == MigrationOutcome.Applied);
        var skipped = results.Count(r => r.Outcome == MigrationOutcome.Skipped);
        var validated = results.Count(r => r.Outcome == MigrationOutcome.Validated);
        var failed = results.Where(r => r.Outcome == MigrationOutcome.Failed).ToList();

        logger.LogInformation(
            "{Label}: {Applied} applied, {Skipped} skipped, {Validated} validated, {Failed} failed",
            label, applied, skipped, validated, failed.Count);

        foreach (var f in failed)
        {
            logger.LogError("{Label}: FAIL {Id} ({Module}) — {Error}",
                label, f.Script.Id, f.Script.Module, f.ErrorMessage);
        }

        if (failed.Count > 0)
        {
            throw new InvalidOperationException(
                $"{label}: {failed.Count} migration(s) failed validation/apply. See log for details.");
        }
    }
}

internal enum CliCommand { Migrate, Validate }
internal enum CliTarget { All, ControlPlane, Tenant }

internal sealed record CliArgs(
    CliCommand Command,
    CliTarget Target,
    string? TenantSlug,
    string? TenantId,
    string? AdoptLegacyPath)
{
    public static CliArgs Parse(string[] args)
    {
        if (args.Length == 0) throw new CliUsageException("No command specified.");

        var cmd = args[0] switch
        {
            "migrate" => CliCommand.Migrate,
            "validate" => CliCommand.Validate,
            _ => throw new CliUsageException($"Unknown command '{args[0]}'."),
        };

        CliTarget? target = null;
        string? slug = null;
        string? id = null;
        string? adoptLegacyPath = null;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--target":
                    target = ReadValue(args, ref i, "--target") switch
                    {
                        "all" => CliTarget.All,
                        "control-plane" => CliTarget.ControlPlane,
                        "tenant" => CliTarget.Tenant,
                        var v => throw new CliUsageException($"Invalid --target value '{v}'."),
                    };
                    break;
                case "--tenant-slug":
                    slug = ReadValue(args, ref i, "--tenant-slug");
                    break;
                case "--tenant-id":
                    id = ReadValue(args, ref i, "--tenant-id");
                    break;
                case "--adopt-legacy":
                    adoptLegacyPath = ReadValue(args, ref i, "--adopt-legacy");
                    break;
                default:
                    throw new CliUsageException($"Unknown argument '{args[i]}'.");
            }
        }

        if (target is null) throw new CliUsageException("--target is required.");
        if (slug is not null && id is not null)
            throw new CliUsageException("Use either --tenant-slug or --tenant-id, not both.");
        if ((slug is not null || id is not null) && target != CliTarget.Tenant)
            throw new CliUsageException("--tenant-slug / --tenant-id only valid with --target tenant.");
        if (adoptLegacyPath is not null && cmd != CliCommand.Migrate)
            throw new CliUsageException("--adopt-legacy is only valid with the 'migrate' command.");

        return new CliArgs(cmd, target.Value, slug, id, adoptLegacyPath);
    }

    private static string ReadValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length) throw new CliUsageException($"{flag} requires a value.");
        return args[++i];
    }
}

internal sealed class CliUsageException : Exception
{
    public CliUsageException(string message) : base(message) { }
}

internal static class CliUsage
{
    public const string Text = """
        Usage:
          migrator migrate  --target all|control-plane|tenant [--tenant-slug SLUG | --tenant-id ID]
                            [--adopt-legacy /path/to/baseline.json]
          migrator validate --target all|control-plane|tenant [--tenant-slug SLUG | --tenant-id ID]

        --adopt-legacy is a one-time cutover flag: marks the listed migration ids as
        already-applied (idempotently) before the run, so a database that was previously
        migrated by a different runner is not re-migrated. See requirements/orkyo-dbup-
        migration-spec-for-copilot.md for the baseline.json shape.

        Required env:
          CONTROL_PLANE_CONNECTION_STRING

        Optional env:
          APP_VERSION                       recorded as applied_by_version
          MIGRATION_LOCK_TIMEOUT_SECONDS    advisory-lock acquisition timeout (default 60)
        """;
}

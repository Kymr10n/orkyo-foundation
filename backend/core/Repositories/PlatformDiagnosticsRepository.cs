using Api.Helpers;
using Api.Services;
using Orkyo.Shared;

namespace Api.Repositories;

/// <summary>Worker liveness as the admin diagnostics page reports it.</summary>
public sealed record WorkerActivity(DateTime? LastSucceededAt, bool SucceededRecently, int WorstConsecutiveFailures);

public interface IPlatformDiagnosticsRepository
{
    Task<(int MigrationsApplied, int Tenants)> GetSchemaCountsAsync(CancellationToken ct = default);
    Task<WorkerActivity> GetWorkerActivityAsync(CancellationToken ct = default);
}

/// <summary>Control-plane reads behind the site-admin diagnostics endpoint.</summary>
public sealed class PlatformDiagnosticsRepository(IDbConnectionFactory connectionFactory) : IPlatformDiagnosticsRepository
{
    public async Task<(int MigrationsApplied, int Tenants)> GetSchemaCountsAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        var migrations = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM orkyo_schema_migrations", null, ct);
        var tenants = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM tenants WHERE status != @deleting",
            p => p.AddWithValue("deleting", TenantStatusConstants.Deleting), ct);
        return ((int)migrations, (int)tenants);
    }

    /// <summary>
    /// The worker journals every scheduled job in worker_job_runs (the API never writes there).
    /// last_succeeded_at survives failures, so "recent" means a job actually succeeded in the
    /// last two hours.
    /// </summary>
    public async Task<WorkerActivity> GetWorkerActivityAsync(CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateControlPlaneConnection();
        return (await conn.QuerySingleOrDefaultAsync(
            "SELECT MAX(last_succeeded_at) AS last_completed, " +
            "COALESCE(MAX(last_succeeded_at) > NOW() - INTERVAL '2 hours', false) AS recent, " +
            "COALESCE(MAX(consecutive_failures), 0) AS worst_failures " +
            "FROM worker_job_runs",
            null,
            reader => new WorkerActivity(
                reader.GetNullableDateTime("last_completed"),
                reader.GetBoolean("recent"),
                reader.GetInt32("worst_failures")),
            ct))!;
    }
}

using System.Security.Cryptography;
using System.Text;
using Api.Integrations.Reporting;
using Api.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Api.Reporting;

public sealed class TenantReportingProvisioner : ITenantReportingProvisioner
{
    private readonly IDbConnectionFactory _db;
    private readonly IReportingEngineClient _engine;
    private readonly ReportingOptions _opts;
    private readonly ILogger<TenantReportingProvisioner> _logger;

    public TenantReportingProvisioner(
        IDbConnectionFactory db,
        IReportingEngineClient engine,
        IOptions<ReportingOptions> opts,
        ILogger<TenantReportingProvisioner> logger)
    {
        _db = db;
        _engine = engine;
        _opts = opts.Value;
        _logger = logger;
    }

    public async Task ProvisionAsync(Guid tenantId, string dbIdentifier, CancellationToken ct = default)
    {
        if (!_opts.Enabled)
        {
            _logger.LogDebug("Reporting disabled — skipping provisioning for tenant {TenantId}", tenantId);
            return;
        }

        _logger.LogInformation("Starting reporting provisioning for tenant {TenantId} ({DbIdentifier})",
            tenantId, dbIdentifier);

        await UpsertStateAsync(tenantId, "provisioning", null, null, ct);

        try
        {
            // 1. Load current credentials_version from state (default 1 for new tenants).
            var credVersion = await GetCredentialsVersionAsync(tenantId, ct);

            // 2. Set reader role password on the tenant DB.
            var readerRole = $"{dbIdentifier}_rpt_reader";
            var readerPassword = DeriveReaderPassword(dbIdentifier, credVersion);
            await SetReaderRolePasswordAsync(dbIdentifier, readerRole, readerPassword, ct);

            // 3. Ensure Superset datasource exists.
            var sqlAlchemyUri = BuildSqlAlchemyUri(dbIdentifier, readerRole, readerPassword);
            var datasourceName = $"{dbIdentifier}_reporting";
            var datasourceUuid = await _engine.EnsureDatabaseAsync(datasourceName, sqlAlchemyUri, ct);

            // 4. Create per-tenant dashboard copies and write bindings.
            await EnsureDashboardBindingsAsync(tenantId, dbIdentifier, datasourceUuid, ct);

            // 5. Mark provisioned.
            await UpsertStateAsync(tenantId, "provisioned", datasourceUuid, null, ct);

            _logger.LogInformation(
                "Reporting provisioning completed for tenant {TenantId} ({DbIdentifier})",
                tenantId, dbIdentifier);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Reporting provisioning failed for tenant {TenantId} ({DbIdentifier})",
                tenantId, dbIdentifier);
            await UpsertStateAsync(tenantId, "failed", null, ex.Message, ct);
            throw;
        }
    }

    // ── Steps ─────────────────────────────────────────────────────────────────

    private async Task SetReaderRolePasswordAsync(
        string dbIdentifier, string readerRole, string password, CancellationToken ct)
    {
        await using var conn = _db.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync(ct);

        // Use NpgsqlCommand.ExecuteNonQueryAsync with a literal — the role name and
        // password are server-generated strings, not user input, so interpolation is safe.
        // NpgsqlParameter can't be used for identifiers / IDENTIFIED BY values in DDL.
        await using var cmd = new NpgsqlCommand(
            $"ALTER ROLE \"{readerRole}\" WITH LOGIN PASSWORD '{EscapePostgresString(password)}'",
            conn);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogDebug("Set reader role password for {Role}", readerRole);
    }

    private async Task EnsureDashboardBindingsAsync(
        Guid tenantId, string dbIdentifier, Guid datasourceUuid, CancellationToken ct)
    {
        if (_opts.TemplateDashboardIds.Count == 0)
        {
            _logger.LogWarning(
                "Reporting:TemplateDashboardIds is empty — dashboard bindings cannot be created for {TenantId}. " +
                "Create template dashboards in Superset and configure their UUIDs, then reprovision.",
                tenantId);
            return;
        }

        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        foreach (var (reportKey, templateUuidStr) in _opts.TemplateDashboardIds)
        {
            if (!Guid.TryParse(templateUuidStr, out var templateUuid))
            {
                _logger.LogWarning("Invalid template dashboard UUID '{Value}' for report key '{Key}'",
                    templateUuidStr, reportKey);
                continue;
            }

            // Check if binding already exists (idempotency).
            await using var checkCmd = new NpgsqlCommand(
                "SELECT dashboard_uuid FROM public.tenant_report_bindings WHERE tenant_id = @tid AND report_key = @key",
                conn);
            checkCmd.Parameters.AddWithValue("tid", tenantId);
            checkCmd.Parameters.AddWithValue("key", reportKey);
            var existing = await checkCmd.ExecuteScalarAsync(ct);
            if (existing is not null and not DBNull)
            {
                _logger.LogDebug("Dashboard binding for {ReportKey}/{TenantId} already exists", reportKey, tenantId);
                continue;
            }

            // Copy template dashboard — produces a per-tenant copy.
            var newTitle = $"{reportKey} — {dbIdentifier}";
            var newDashboardUuid = await _engine.CopyDashboardAsync(templateUuid, newTitle, ct);

            // Write binding.
            await using var insertCmd = new NpgsqlCommand(@"
                INSERT INTO public.tenant_report_bindings (tenant_id, report_key, dashboard_uuid)
                VALUES (@tid, @key, @uuid)
                ON CONFLICT (tenant_id, report_key) DO UPDATE SET dashboard_uuid = EXCLUDED.dashboard_uuid",
                conn);
            insertCmd.Parameters.AddWithValue("tid", tenantId);
            insertCmd.Parameters.AddWithValue("key", reportKey);
            insertCmd.Parameters.AddWithValue("uuid", newDashboardUuid);
            await insertCmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "Created dashboard binding for report '{ReportKey}' tenant {TenantId}: {DashboardUuid}",
                reportKey, tenantId, newDashboardUuid);
        }
    }

    // ── Control-plane state ───────────────────────────────────────────────────

    private async Task<int> GetCredentialsVersionAsync(Guid tenantId, CancellationToken ct)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT credentials_version FROM public.tenant_reporting_state WHERE tenant_id = @tid",
            conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int v ? v : 1;
    }

    private async Task UpsertStateAsync(
        Guid tenantId, string status, Guid? datasourceUuid, string? lastError, CancellationToken ct)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO public.tenant_reporting_state
                (tenant_id, status, datasource_uuid, last_provisioned_at, last_error)
            VALUES (@tid, @status, @dsUuid, CASE WHEN @status = 'provisioned' THEN NOW() ELSE NULL END, @err)
            ON CONFLICT (tenant_id) DO UPDATE SET
                status               = EXCLUDED.status,
                datasource_uuid      = COALESCE(EXCLUDED.datasource_uuid, tenant_reporting_state.datasource_uuid),
                last_provisioned_at  = CASE WHEN EXCLUDED.status = 'provisioned' THEN NOW()
                                            ELSE tenant_reporting_state.last_provisioned_at END,
                last_error           = EXCLUDED.last_error",
            conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("dsUuid", (object?)datasourceUuid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("err", (object?)lastError ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Credential derivation ─────────────────────────────────────────────────

    internal string DeriveReaderPassword(string dbIdentifier, int credentialsVersion)
    {
        var key = Encoding.UTF8.GetBytes(_opts.ReaderCredentialMasterSecret);
        var message = Encoding.UTF8.GetBytes($"{dbIdentifier}:{credentialsVersion}");
        var hash = HMACSHA256.HashData(key, message);
        // 32 chars of URL-safe base64 (no padding) → safe for a Postgres password.
        return Convert.ToBase64String(hash)[..32]
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private string BuildSqlAlchemyUri(string dbIdentifier, string readerRole, string password) =>
        $"postgresql+psycopg2://{readerRole}:{Uri.EscapeDataString(password)}" +
        $"@{_opts.PostgresHost}:{_opts.PostgresPort}/{dbIdentifier}";

    // Single-quotes inside a Postgres string literal are escaped by doubling them.
    private static string EscapePostgresString(string s) => s.Replace("'", "''");
}

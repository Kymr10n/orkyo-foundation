using Api.Reporting;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public sealed class ReportBindingRepository : IReportBindingRepository
{
    private readonly IDbConnectionFactory _db;

    public ReportBindingRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Guid?> GetDashboardUuidAsync(
        Guid tenantId, string reportKey, CancellationToken ct = default)
    {
        await using var conn = _db.CreateControlPlaneConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "SELECT dashboard_uuid FROM public.tenant_report_bindings WHERE tenant_id = @tid AND report_key = @key",
            conn);
        cmd.Parameters.AddWithValue("tid", tenantId);
        cmd.Parameters.AddWithValue("key", reportKey);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is Guid uuid ? uuid : null;
    }
}

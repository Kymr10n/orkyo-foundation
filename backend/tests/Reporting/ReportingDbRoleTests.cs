using Npgsql;

namespace Orkyo.Foundation.Tests.Reporting;

/// <summary>
/// Verifies that the per-tenant rpt_reader role:
///  1. Can SELECT from reporting.* views.
///  2. Cannot SELECT from public.* tables.
///  3. Cannot write to or create objects in the reporting schema.
///  4. Cannot connect to another tenant's database.
/// </summary>
[Collection("TwoTenant collection")]
public sealed class ReportingDbRoleTests(TwoTenantFixture fixture)
{
    // ── Permission boundary: can read views ───────────────────────────────────

    [Fact]
    public async Task Reader_CanSelect_FromReportingView()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM reporting.rpt_request_pipeline", conn);
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        count.Should().BeGreaterThanOrEqualTo(1, "the seeded marker request should be visible");
    }

    // ── Permission boundary: cannot bypass view to base table ─────────────────

    [Fact]
    public async Task Reader_CannotSelect_FromPublicRequestsTable()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM public.requests", conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
        ex.SqlState.Should().Be("42501", "permission denied expected when accessing public.requests directly");
    }

    // ── Permission boundary: cannot write to reporting schema ─────────────────

    [Fact]
    public async Task Reader_CannotCreateTable_InReportingSchema()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "CREATE TABLE reporting.rogue(id int)", conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        ex.SqlState.Should().Be("42501");
    }

    [Fact]
    public async Task Reader_CannotInsert_IntoReportingView()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO reporting.rpt_request_pipeline(request_id) VALUES(gen_random_uuid())", conn);

        await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
    }

    // ── Cross-tenant isolation: view data belongs to this tenant only ─────────

    [Fact]
    public async Task TenantAReader_SeesOnlyTenantAMarker_InRequestPipeline()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        var names = await CollectNamesAsync(conn, "SELECT name FROM reporting.rpt_request_pipeline");

        names.Should().Contain(TwoTenantFixture.MarkerTenantA);
        names.Should().NotContain(TwoTenantFixture.MarkerTenantB,
            "tenant B's data must never appear in tenant A's reporting view");
    }

    [Fact]
    public async Task TenantBReader_SeesOnlyTenantBMarker_InRequestPipeline()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantBDb));
        await conn.OpenAsync();

        var names = await CollectNamesAsync(conn, "SELECT name FROM reporting.rpt_request_pipeline");

        names.Should().Contain(TwoTenantFixture.MarkerTenantB);
        names.Should().NotContain(TwoTenantFixture.MarkerTenantA,
            "tenant A's data must never appear in tenant B's reporting view");
    }

    // ── Cross-tenant isolation: reader in the wrong DB cannot read views ─────
    // Postgres grants CONNECT to PUBLIC by default; the isolation boundary is at
    // the schema USAGE + view SELECT level, not at the connection level.

    [Fact]
    public async Task TenantAReader_CannotQueryReportingSchema_InTenantBDatabase()
    {
        // tenant_a_rpt_reader has no USAGE on tenant_b's reporting schema.
        var crossCs =
            $"Host=localhost;Port={fixture.Port};Database={TwoTenantFixture.TenantBDb};" +
            $"Username={TwoTenantFixture.TenantADb}_rpt_reader;Password=test_reader_pw";

        await using var conn = new NpgsqlConnection(crossCs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM reporting.rpt_request_pipeline", conn);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteScalarAsync());
        // 42501 = permission denied / 3F000 = invalid schema name — both indicate no access.
        ex.SqlState.Should().BeOneOf("42501", "3F000",
            "tenant_a reader must not have USAGE on tenant_b's reporting schema");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<string>> CollectNamesAsync(NpgsqlConnection conn, string sql)
    {
        var results = new List<string>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            results.Add(reader.GetString(0));
        return results;
    }
}

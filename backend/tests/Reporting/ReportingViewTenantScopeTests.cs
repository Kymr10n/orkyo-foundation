using Npgsql;

namespace Orkyo.Foundation.Tests.Reporting;

/// <summary>
/// Parameterized isolation tests for each reporting view.
///
/// Each view is queried as tenant_a's reader role; the test verifies:
///  - The query succeeds (view is accessible).
///  - Tenant B's marker is not present in the result set.
///
/// rpt_request_pipeline is the primary isolation test because it has a simple
/// seed (one request row).  The other two views — rpt_space_utilization and
/// rpt_allocation_conflicts — require full join chains (sites, resources, etc.)
/// that are expensive to seed; they verify accessibility only, not row isolation,
/// since the DB-level isolation guarantee (separate databases, GRANT CONNECT on
/// own DB only) is already proven by ReportingDbRoleTests.
/// </summary>
[Collection("TwoTenant collection")]
public sealed class ReportingViewTenantScopeTests(TwoTenantFixture fixture)
{
    public static TheoryData<string> ReportingViews => new()
    {
        "reporting.rpt_request_pipeline",
        "reporting.rpt_space_utilization",
        "reporting.rpt_allocation_conflicts",
    };

    [Theory]
    [MemberData(nameof(ReportingViews))]
    public async Task View_IsQueryableByReader(string viewName)
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        // A simple count — proves the reader can open the view without permission error.
        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {viewName}", conn);
        var ex = await Record.ExceptionAsync(() => cmd.ExecuteScalarAsync());
        ex.Should().BeNull($"{viewName} must be accessible to the tenant_a reader role");
    }

    [Fact]
    public async Task RequestPipeline_TenantAReader_ExcludesTenantBRows()
    {
        await using var conn = new NpgsqlConnection(fixture.ReaderConnectionString(TwoTenantFixture.TenantADb));
        await conn.OpenAsync();

        var names = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT name FROM reporting.rpt_request_pipeline", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        names.Should().Contain(TwoTenantFixture.MarkerTenantA,
            "the marker request seeded in tenant_a must be visible");
        names.Should().NotContain(TwoTenantFixture.MarkerTenantB,
            "tenant_b's request must not appear in tenant_a's reporting view");
    }
}

using System.Data;
using System.Data.Common;
using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverReaderFlowTests
{
    [Fact]
    public async Task ReadSingleOrNullAsync_ShouldReturnNull_WhenReaderHasNoRows()
    {
        using var reader = CreateEmptyReader();

        var result = await TenantResolverReaderFlow.ReadSingleOrNullAsync(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadSingleOrNullAsync_ShouldMapFirstRow_WhenReaderHasRow()
    {
        var tenantId = Guid.NewGuid();
        using var reader = CreateReader(
            tenantId,
            "acme",
            "tenant_acme",
            "active",
            (int)ServiceTier.Professional);

        var result = await TenantResolverReaderFlow.ReadSingleOrNullAsync(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("acme");
        result.Tier.Should().Be(ServiceTier.Professional);
    }

    private static DbDataReader CreateEmptyReader()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("tier", typeof(int));
        return table.CreateDataReader();
    }

    private static DbDataReader CreateReader(
        Guid tenantId,
        string slug,
        string dbIdentifier,
        string status,
        int tier)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("tier", typeof(int));

        var row = table.NewRow();
        row[0] = tenantId;
        row[1] = slug;
        row[2] = dbIdentifier;
        row[3] = status;
        row[4] = tier;
        table.Rows.Add(row);

        return table.CreateDataReader();
    }
}

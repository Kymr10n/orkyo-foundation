using System.Data;
using System.Data.Common;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantResolverDbFlowTests
{
    [Fact]
    public async Task ResolveFromOpenConnectionAsync_ShouldReturnNull_WhenReaderHasNoRows()
    {
        using var connection = new NpgsqlConnection();

        var result = await TenantResolverDbFlow.ResolveFromOpenConnectionAsync(
            connection,
            "acme",
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres",
            commandFactory: static (_, _) => new NpgsqlCommand(),
            readerExecutor: static _ => Task.FromResult<DbDataReader>(CreateEmptyReader()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveFromOpenConnectionAsync_ShouldMapReaderRow_WhenReaderHasData()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();

        var result = await TenantResolverDbFlow.ResolveFromOpenConnectionAsync(
            connection,
            "acme",
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres",
            commandFactory: static (_, _) => new NpgsqlCommand(),
            readerExecutor: _ => Task.FromResult<DbDataReader>(CreateRowReader(tenantId, "acme", "tenant_acme", "active", (int)ServiceTier.Professional, DBNull.Value)));

        result.Should().NotBeNull();
        result!.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("acme");
        result.Tier.Should().Be(ServiceTier.Professional);
        result.SuspensionReason.Should().BeNull();
    }

    private static DbDataReader CreateEmptyReader()
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("tier", typeof(int));
        table.Columns.Add("suspension_reason", typeof(string));
        return table.CreateDataReader();
    }

    private static DbDataReader CreateRowReader(
        Guid tenantId,
        string slug,
        string dbIdentifier,
        string status,
        int tier,
        object suspensionReason)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("tier", typeof(int));
        table.Columns.Add("suspension_reason", typeof(string));

        var row = table.NewRow();
        row[0] = tenantId;
        row[1] = slug;
        row[2] = dbIdentifier;
        row[3] = status;
        row[4] = tier;
        row[5] = suspensionReason;
        table.Rows.Add(row);

        return table.CreateDataReader();
    }
}
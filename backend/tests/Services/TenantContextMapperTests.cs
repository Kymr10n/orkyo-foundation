using System.Data;
using System.Data.Common;
using Api.Models;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantContextMapperTests
{
    [Fact]
    public void MapFromResolverRow_ShouldMapAllFields_WhenSuspensionReasonPresent()
    {
        var tenantId = Guid.NewGuid();
        const string slug = "acme";
        const string dbIdentifier = "tenant_acme";
        const string status = "suspended";
        const int tier = (int)ServiceTier.Professional;
        const string suspensionReason = "inactivity";

        using var reader = CreateReader(tenantId, slug, dbIdentifier, status, tier, suspensionReason);
        reader.Read().Should().BeTrue();

        var mapped = TenantContextMapper.MapFromResolverRow(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        mapped.TenantId.Should().Be(tenantId);
        mapped.TenantSlug.Should().Be(slug);
        mapped.Status.Should().Be(status);
        mapped.Tier.Should().Be(ServiceTier.Professional);
        mapped.SuspensionReason.Should().Be(suspensionReason);
        mapped.TenantDbConnectionString.Should().Contain("Database=tenant_acme");
    }

    [Fact]
    public void MapFromResolverRow_ShouldSetNullSuspensionReason_WhenDbNull()
    {
        using var reader = CreateReader(
            Guid.NewGuid(),
            "blue",
            "tenant_blue",
            "active",
            (int)ServiceTier.Free,
            DBNull.Value);
        reader.Read().Should().BeTrue();

        var mapped = TenantContextMapper.MapFromResolverRow(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        mapped.SuspensionReason.Should().BeNull();
        mapped.Tier.Should().Be(ServiceTier.Free);
    }

    private static DbDataReader CreateReader(
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
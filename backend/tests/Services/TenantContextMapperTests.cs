using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantContextMapperTests
{
    [Fact]
    public void MapFromResolverRow_ShouldMapAllFields()
    {
        var tenantId = Guid.NewGuid();
        const string slug = "acme";
        const string dbIdentifier = "tenant_acme";
        const string status = "suspended";

        using var reader = CreateReader(tenantId, slug, dbIdentifier, status);
        reader.Read().Should().BeTrue();

        var mapped = TenantContextMapper.MapFromResolverRow(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        mapped.TenantId.Should().Be(tenantId);
        mapped.TenantSlug.Should().Be(slug);
        mapped.Status.Should().Be(status);
        mapped.TenantDbConnectionString.Should().Contain("Database=tenant_acme");
    }

    [Fact]
    public void MapFromResolverRow_ShouldMapStatus_ForActiveTenant()
    {
        using var reader = CreateReader(Guid.NewGuid(), "blue", "tenant_blue", "active");
        reader.Read().Should().BeTrue();

        var mapped = TenantContextMapper.MapFromResolverRow(
            reader,
            "Host=localhost;Database=control_plane;Username=postgres;Password=postgres");

        mapped.Status.Should().Be("active");
    }

    private static DbDataReader CreateReader(Guid tenantId, string slug, string dbIdentifier, string status)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("status", typeof(string));

        var row = table.NewRow();
        row[0] = tenantId;
        row[1] = slug;
        row[2] = dbIdentifier;
        row[3] = status;
        table.Rows.Add(row);

        return table.CreateDataReader();
    }
}

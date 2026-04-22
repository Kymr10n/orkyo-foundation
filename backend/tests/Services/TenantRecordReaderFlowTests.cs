using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantRecordReaderFlowTests
{
    [Fact]
    public async Task ReadSingleOrNullAsync_ShouldReturnNull_WhenNoRows()
    {
        using var reader = CreateReader();

        var result = await TenantRecordReaderFlow.ReadSingleOrNullAsync(reader);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReadSingleOrNullAsync_ShouldMapTenantSnapshot_WhenRowExists()
    {
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        using var reader = CreateReader((tenantId, "acme", "Acme", "active", "tenant_acme", ownerUserId, createdAt));

        var result = await TenantRecordReaderFlow.ReadSingleOrNullAsync(reader);

        result.Should().NotBeNull();
        result!.Value.Id.Should().Be(tenantId);
        result.Value.Slug.Should().Be("acme");
        result.Value.DisplayName.Should().Be("Acme");
        result.Value.Status.Should().Be("active");
        result.Value.DbIdentifier.Should().Be("tenant_acme");
        result.Value.OwnerUserId.Should().Be(ownerUserId);
        result.Value.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void ReadCurrent_ShouldAllowNullOwner()
    {
        var tenantId = Guid.NewGuid();
        var createdAt = DateTime.UtcNow;
        using var reader = CreateReader((tenantId, "acme", "Acme", "active", "tenant_acme", null, createdAt));
        reader.Read();

        var result = TenantRecordReaderFlow.ReadCurrent(reader);

        result.OwnerUserId.Should().BeNull();
    }

    private static DbDataReader CreateReader((Guid Id, string Slug, string DisplayName, string Status, string DbIdentifier, Guid? OwnerUserId, DateTime CreatedAt)? row = null)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("display_name", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("db_identifier", typeof(string));
        table.Columns.Add("owner_user_id", typeof(Guid));
        table.Columns.Add("created_at", typeof(DateTime));

        if (row.HasValue)
        {
            var dataRow = table.NewRow();
            dataRow[0] = row.Value.Id;
            dataRow[1] = row.Value.Slug;
            dataRow[2] = row.Value.DisplayName;
            dataRow[3] = row.Value.Status;
            dataRow[4] = row.Value.DbIdentifier;
            dataRow[5] = row.Value.OwnerUserId.HasValue ? row.Value.OwnerUserId.Value : DBNull.Value;
            dataRow[6] = row.Value.CreatedAt;
            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }
}

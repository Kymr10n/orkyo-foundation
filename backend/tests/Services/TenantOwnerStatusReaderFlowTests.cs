using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantOwnerStatusReaderFlowTests
{
    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldReturnNotFound_WhenNoRows()
    {
        using var reader = CreateReader();

        var result = await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeFalse();
        result.OwnerId.Should().BeNull();
        result.Status.Should().BeNull();
    }

    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldReturnMappedSnapshot_WhenRowExists()
    {
        var ownerId = Guid.NewGuid();
        using var reader = CreateReader((ownerId, "active"));

        var result = await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeTrue();
        result.OwnerId.Should().Be(ownerId);
        result.Status.Should().Be("active");
    }

    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldAllowNullOwner_WhenOwnerIsNull()
    {
        using var reader = CreateReader((null, "active"));

        var result = await TenantOwnerStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeTrue();
        result.OwnerId.Should().BeNull();
        result.Status.Should().Be("active");
    }

    private static DbDataReader CreateReader((Guid? OwnerId, string Status)? row = null)
    {
        var table = new DataTable();
        table.Columns.Add("owner_user_id", typeof(Guid));
        table.Columns.Add("status", typeof(string));

        if (row.HasValue)
        {
            var dataRow = table.NewRow();
            dataRow[0] = row.Value.OwnerId.HasValue ? row.Value.OwnerId.Value : DBNull.Value;
            dataRow[1] = row.Value.Status;
            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }
}

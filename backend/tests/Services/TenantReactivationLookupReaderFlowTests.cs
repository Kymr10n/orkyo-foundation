using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantReactivationLookupReaderFlowTests
{
    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldReturnNotFound_WhenNoRows()
    {
        using var reader = CreateReader();

        var result = await TenantReactivationLookupReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeFalse();
        result.Status.Should().BeNull();
        result.SuspensionReason.Should().BeNull();
        result.Role.Should().BeNull();
        result.OwnerUserId.Should().BeNull();
    }

    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldMapAllColumns_WhenRowExists()
    {
        var ownerUserId = Guid.NewGuid();
        using var reader = CreateReader(("suspended", "billing", "admin", ownerUserId));

        var result = await TenantReactivationLookupReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeTrue();
        result.Status.Should().Be("suspended");
        result.SuspensionReason.Should().Be("billing");
        result.Role.Should().Be("admin");
        result.OwnerUserId.Should().Be(ownerUserId);
    }

    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldAllowNullSuspensionReasonAndOwner()
    {
        using var reader = CreateReader(("suspended", null, "member", null));

        var result = await TenantReactivationLookupReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeTrue();
        result.SuspensionReason.Should().BeNull();
        result.OwnerUserId.Should().BeNull();
    }

    private static DbDataReader CreateReader((string Status, string? SuspensionReason, string Role, Guid? OwnerUserId)? row = null)
    {
        var table = new DataTable();
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("suspension_reason", typeof(string));
        table.Columns.Add("role", typeof(string));
        table.Columns.Add("owner_user_id", typeof(Guid));

        if (row.HasValue)
        {
            var dataRow = table.NewRow();
            dataRow[0] = row.Value.Status;
            dataRow[1] = row.Value.SuspensionReason ?? (object)DBNull.Value;
            dataRow[2] = row.Value.Role;
            dataRow[3] = row.Value.OwnerUserId.HasValue ? row.Value.OwnerUserId.Value : DBNull.Value;
            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }
}

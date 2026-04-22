using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipRoleStatusReaderFlowTests
{
    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldReturnNotFound_WhenNoRows()
    {
        using var reader = CreateReader();

        var result = await TenantMembershipRoleStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeFalse();
        result.Role.Should().BeNull();
        result.MembershipStatus.Should().BeNull();
    }

    [Fact]
    public async Task ReadSingleOrNotFoundAsync_ShouldReturnMappedSnapshot_WhenRowExists()
    {
        using var reader = CreateReader(("admin", "active"));

        var result = await TenantMembershipRoleStatusReaderFlow.ReadSingleOrNotFoundAsync(reader);

        result.Found.Should().BeTrue();
        result.Role.Should().Be("admin");
        result.MembershipStatus.Should().Be("active");
    }

    private static DbDataReader CreateReader((string Role, string Status)? row = null)
    {
        var table = new DataTable();
        table.Columns.Add("role", typeof(string));
        table.Columns.Add("status", typeof(string));

        if (row.HasValue)
        {
            var dataRow = table.NewRow();
            dataRow[0] = row.Value.Role;
            dataRow[1] = row.Value.Status;
            table.Rows.Add(dataRow);
        }

        return table.CreateDataReader();
    }
}

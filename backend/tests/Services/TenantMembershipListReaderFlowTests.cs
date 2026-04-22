using System.Data;
using System.Data.Common;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMembershipListReaderFlowTests
{
    [Fact]
    public void ReadCurrent_ShouldMapRow_WithOwner()
    {
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow;
        using var reader = CreateReader((tenantId, "acme", "Acme", "active", ownerUserId, "admin", "active", joinedAt));
        reader.Read();

        var snapshot = TenantMembershipListReaderFlow.ReadCurrent(reader);

        snapshot.TenantId.Should().Be(tenantId);
        snapshot.TenantSlug.Should().Be("acme");
        snapshot.TenantDisplayName.Should().Be("Acme");
        snapshot.TenantStatus.Should().Be("active");
        snapshot.OwnerUserId.Should().Be(ownerUserId);
        snapshot.Role.Should().Be("admin");
        snapshot.MembershipStatus.Should().Be("active");
        snapshot.JoinedAt.Should().Be(joinedAt);
    }

    [Fact]
    public void ReadCurrent_ShouldAllowNullOwner()
    {
        var tenantId = Guid.NewGuid();
        var joinedAt = DateTime.UtcNow;
        using var reader = CreateReader((tenantId, "acme", "Acme", "active", null, "member", "active", joinedAt));
        reader.Read();

        var snapshot = TenantMembershipListReaderFlow.ReadCurrent(reader);

        snapshot.OwnerUserId.Should().BeNull();
    }

    private static DbDataReader CreateReader((Guid TenantId, string TenantSlug, string TenantDisplayName, string TenantStatus, Guid? OwnerUserId, string Role, string MembershipStatus, DateTime JoinedAt) row)
    {
        var table = new DataTable();
        table.Columns.Add("id", typeof(Guid));
        table.Columns.Add("slug", typeof(string));
        table.Columns.Add("display_name", typeof(string));
        table.Columns.Add("status", typeof(string));
        table.Columns.Add("owner_user_id", typeof(Guid));
        table.Columns.Add("role", typeof(string));
        table.Columns.Add("tm_status", typeof(string));
        table.Columns.Add("created_at", typeof(DateTime));

        var dataRow = table.NewRow();
        dataRow[0] = row.TenantId;
        dataRow[1] = row.TenantSlug;
        dataRow[2] = row.TenantDisplayName;
        dataRow[3] = row.TenantStatus;
        dataRow[4] = row.OwnerUserId.HasValue ? row.OwnerUserId.Value : DBNull.Value;
        dataRow[5] = row.Role;
        dataRow[6] = row.MembershipStatus;
        dataRow[7] = row.JoinedAt;
        table.Rows.Add(dataRow);

        return table.CreateDataReader();
    }
}

using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class TenantLeaveLookupScalarFlowTests
{
    [Fact]
    public void ReadOwnerUserId_ShouldReturnGuid_WhenScalarIsGuid()
    {
        var ownerId = Guid.NewGuid();

        TenantLeaveLookupScalarFlow.ReadOwnerUserId(ownerId).Should().Be(ownerId);
    }

    [Fact]
    public void ReadOwnerUserId_ShouldReturnNull_WhenScalarIsNotGuid()
    {
        TenantLeaveLookupScalarFlow.ReadOwnerUserId(null).Should().BeNull();
    }

    [Fact]
    public void ReadActiveAdminCount_ShouldReturnZero_WhenScalarIsNull()
    {
        TenantLeaveLookupScalarFlow.ReadActiveAdminCount(null).Should().Be(0);
    }

    [Fact]
    public void ReadActiveAdminCount_ShouldHandleLongAndInt()
    {
        TenantLeaveLookupScalarFlow.ReadActiveAdminCount(2L).Should().Be(2);
        TenantLeaveLookupScalarFlow.ReadActiveAdminCount(3).Should().Be(3);
    }

    [Fact]
    public void ReadActiveRole_ShouldReturnStringOrNull()
    {
        TenantLeaveLookupScalarFlow.ReadActiveRole("admin").Should().Be("admin");
        TenantLeaveLookupScalarFlow.ReadActiveRole(123).Should().BeNull();
    }
}

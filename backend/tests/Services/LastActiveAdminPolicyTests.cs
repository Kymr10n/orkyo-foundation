using Api.Constants;
using Api.Services;

namespace Orkyo.Foundation.Tests.Services;

public class LastActiveAdminPolicyTests
{
    [Fact]
    public void IsLastActiveAdmin_NullRole_ReturnsFalse()
    {
        LastActiveAdminPolicy.IsLastActiveAdmin(null, 1).Should().BeFalse();
    }

    [Fact]
    public void IsLastActiveAdmin_NonAdminRole_ReturnsFalse()
    {
        LastActiveAdminPolicy.IsLastActiveAdmin("user", 1).Should().BeFalse();
    }

    [Fact]
    public void IsLastActiveAdmin_AdminWithSelfOnly_ReturnsTrue()
    {
        LastActiveAdminPolicy.IsLastActiveAdmin(RoleConstants.Admin, 1).Should().BeTrue();
    }

    [Fact]
    public void IsLastActiveAdmin_AdminWithOtherAdmins_ReturnsFalse()
    {
        LastActiveAdminPolicy.IsLastActiveAdmin(RoleConstants.Admin, 2).Should().BeFalse();
    }

    [Fact]
    public void IsLastActiveAdmin_AdminWithZeroCount_ReturnsTrue()
    {
        // Defensive: if the role lookup says admin but count says 0 (race), still treat as last
        LastActiveAdminPolicy.IsLastActiveAdmin(RoleConstants.Admin, 0).Should().BeTrue();
    }

    [Fact]
    public void IsLastActiveAdmin_AdminCaseInsensitive()
    {
        LastActiveAdminPolicy.IsLastActiveAdmin("ADMIN", 1).Should().BeTrue();
        LastActiveAdminPolicy.IsLastActiveAdmin("Admin", 1).Should().BeTrue();
    }
}

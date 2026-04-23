using Api.Middleware;
using Api.Security;

namespace Orkyo.Foundation.Tests.Middleware;

public class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 2)]
    public void GetMembershipDenialKind_ReturnsExpectedKind(
        bool isAuthenticated,
        bool isSiteAdmin,
        int expected)
    {
        var result = AuthorizationPolicy.GetMembershipDenialKind(isAuthenticated, isSiteAdmin);

        ((int)result).Should().Be(expected);
    }

    [Theory]
    [InlineData(TenantRole.Admin, true)]
    [InlineData(TenantRole.Editor, true)]
    [InlineData(TenantRole.Viewer, false)]
    [InlineData(TenantRole.None, false)]
    public void IsRoleAllowed_RespectsRequiredRoles(TenantRole tenantRole, bool expected)
    {
        var result = AuthorizationPolicy.IsRoleAllowed(tenantRole, TenantRole.Admin, TenantRole.Editor);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void HasAdminAccess_ReturnsExpectedDecision(
        bool isAuthenticated,
        bool isSiteAdmin,
        bool isTenantAdminMember,
        bool expected)
    {
        var result = AuthorizationPolicy.HasAdminAccess(isAuthenticated, isSiteAdmin, isTenantAdminMember);

        result.Should().Be(expected);
    }
}

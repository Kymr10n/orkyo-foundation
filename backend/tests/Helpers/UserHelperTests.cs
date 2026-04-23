using Api.Helpers;
using Api.Models;

namespace Orkyo.Foundation.Tests.Helpers;

public class UserHelperTests
{
    // --- ParseUserStatus ---

    [Theory]
    [InlineData("active", UserStatus.Active)]
    [InlineData("ACTIVE", UserStatus.Active)]
    [InlineData("disabled", UserStatus.Disabled)]
    [InlineData("DISABLED", UserStatus.Disabled)]
    [InlineData("pending_verification", UserStatus.PendingVerification)]
    [InlineData("PENDING_VERIFICATION", UserStatus.PendingVerification)]
    public void ParseUserStatus_ShouldParseCaseInsensitive(string input, UserStatus expected) =>
        UserHelper.ParseUserStatus(input).Should().Be(expected);

    [Theory]
    [InlineData("unknown")]
    [InlineData("banned")]
    [InlineData("")]
    public void ParseUserStatus_ShouldThrow_ForUnknownStatus(string input)
    {
        var act = () => UserHelper.ParseUserStatus(input);
        act.Should().Throw<ArgumentException>().WithMessage($"*{input}*");
    }

    // --- ParseUserRole ---

    [Theory]
    [InlineData("admin", UserRole.Admin)]
    [InlineData("ADMIN", UserRole.Admin)]
    [InlineData("editor", UserRole.Editor)]
    [InlineData("EDITOR", UserRole.Editor)]
    [InlineData("viewer", UserRole.Viewer)]
    [InlineData("VIEWER", UserRole.Viewer)]
    public void ParseUserRole_ShouldParseCaseInsensitive(string input, UserRole expected) =>
        UserHelper.ParseUserRole(input).Should().Be(expected);

    [Theory]
    [InlineData("none")]
    [InlineData("superadmin")]
    [InlineData("")]
    public void ParseUserRole_ShouldThrow_ForUnknownRole(string input)
    {
        var act = () => UserHelper.ParseUserRole(input);
        act.Should().Throw<ArgumentException>().WithMessage($"*{input}*");
    }

    // --- UserSelectColumns contract drift guard ---

    [Fact]
    public void UserSelectColumns_ContainsAllExpectedColumnsInOrder()
    {
        UserHelper.UserSelectColumns.Should().Contain("u.id");
        UserHelper.UserSelectColumns.Should().Contain("u.email");
        UserHelper.UserSelectColumns.Should().Contain("u.display_name");
        UserHelper.UserSelectColumns.Should().Contain("u.status");
        UserHelper.UserSelectColumns.Should().Contain("tm.role");
        UserHelper.UserSelectColumns.Should().Contain("u.created_at");
        UserHelper.UserSelectColumns.Should().Contain("u.updated_at");
        UserHelper.UserSelectColumns.Should().Contain("u.last_login_at");
    }
}

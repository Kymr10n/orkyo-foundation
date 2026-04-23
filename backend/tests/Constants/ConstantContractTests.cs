using Api.Constants;
using Api.Security;

namespace Orkyo.Foundation.Tests.Constants;

/// <summary>
/// Drift guards: assert that backend constant string values match the cross-product
/// contracts consumed by the frontend. Failures here indicate a breaking change that
/// must be coordinated with the frontend before deploying.
/// </summary>
public class ConstantContractTests
{
    // --- ErrorCodes (backend ↔ frontend/contracts/errorCodes.ts) ---

    [Fact]
    public void ErrorCodes_NotFound_ShouldMatchContract() =>
        ErrorCodes.NotFound.Should().Be("NOT_FOUND");

    [Fact]
    public void ErrorCodes_ValidationError_ShouldMatchContract() =>
        ErrorCodes.ValidationError.Should().Be("VALIDATION_ERROR");

    [Fact]
    public void ErrorCodes_Conflict_ShouldMatchContract() =>
        ErrorCodes.Conflict.Should().Be("CONFLICT");

    // --- ApiErrorCodes (backend ↔ frontend/contracts/claims.ts or api-error-codes.ts) ---

    [Fact]
    public void ApiErrorCodes_SessionExpired_ShouldMatchContract() =>
        ApiErrorCodes.SessionExpired.Should().Be("session_expired");

    [Fact]
    public void ApiErrorCodes_BreakGlassExpired_ShouldMatchContract() =>
        ApiErrorCodes.BreakGlassExpired.Should().Be("break_glass_expired");

    [Fact]
    public void ApiErrorCodes_BreakGlassHardCapReached_ShouldMatchContract() =>
        ApiErrorCodes.BreakGlassHardCapReached.Should().Be("break_glass_hard_cap_reached");

    [Fact]
    public void ApiErrorCodes_Forbidden_ShouldMatchContract() =>
        ApiErrorCodes.Forbidden.Should().Be("forbidden");

    [Fact]
    public void ApiErrorCodes_TenantSuspended_ShouldMatchContract() =>
        ApiErrorCodes.TenantSuspended.Should().Be("tenant_suspended");

    // --- RoleConstants string values (backend ↔ frontend/contracts/roles.ts) ---

    [Fact]
    public void RoleConstants_Admin_ShouldMatchContract() =>
        RoleConstants.Admin.Should().Be("admin");

    [Fact]
    public void RoleConstants_Editor_ShouldMatchContract() =>
        RoleConstants.Editor.Should().Be("editor");

    [Fact]
    public void RoleConstants_Viewer_ShouldMatchContract() =>
        RoleConstants.Viewer.Should().Be("viewer");

    [Fact]
    public void RoleConstants_None_ShouldMatchContract() =>
        RoleConstants.None.Should().Be("none");

    // --- RoleConstants.ParseRoleString ---

    [Theory]
    [InlineData("admin", TenantRole.Admin)]
    [InlineData("ADMIN", TenantRole.Admin)]
    [InlineData("Admin", TenantRole.Admin)]
    [InlineData("editor", TenantRole.Editor)]
    [InlineData("EDITOR", TenantRole.Editor)]
    [InlineData("viewer", TenantRole.Viewer)]
    [InlineData("VIEWER", TenantRole.Viewer)]
    public void ParseRoleString_ShouldParseKnownRolesCaseInsensitive(string input, TenantRole expected) =>
        RoleConstants.ParseRoleString(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("superadmin")]
    public void ParseRoleString_ShouldReturnNone_ForUnknownOrNullInput(string? input) =>
        RoleConstants.ParseRoleString(input).Should().Be(TenantRole.None);

    // --- RoleConstants.IsValidRole ---

    [Theory]
    [InlineData("admin")]
    [InlineData("editor")]
    [InlineData("viewer")]
    [InlineData("ADMIN")]
    [InlineData("Viewer")]
    public void IsValidRole_ShouldReturnTrue_ForKnownRoles(string input) =>
        RoleConstants.IsValidRole(input).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("superadmin")]
    [InlineData("  admin  ")]
    public void IsValidRole_ShouldReturnFalse_ForInvalidOrNullInput(string? input) =>
        RoleConstants.IsValidRole(input).Should().BeFalse();
}

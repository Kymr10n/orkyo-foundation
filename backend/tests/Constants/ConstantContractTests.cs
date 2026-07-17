using Api.Constants;
using Api.Helpers;
using Api.Models;
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
        // "ValidationError" is what has always been emitted on the wire (formerly via nameof in
        // ErrorResponses.BadRequest); casing alignment to "VALIDATION_ERROR" is deferred to the
        // next major. The frontend contract mirror must be updated to this value alongside.
        ErrorCodes.ValidationError.Should().Be("ValidationError");

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

    // --- RequestStatuses ↔ RequestStatus enum (DB string == JsonStringEnumMemberName) ---
    // The catalog constants must equal the enum's DB string values, since the SQL/in-memory
    // comparisons in RequestRepository / InsightsService / ReportingQueryService use the constants
    // while the DB stores the enum's JsonStringEnumMemberName value.

    [Theory]
    [InlineData(RequestStatuses.New, RequestStatus.New)]
    [InlineData(RequestStatuses.InProgress, RequestStatus.InProgress)]
    [InlineData(RequestStatuses.Done, RequestStatus.Done)]
    [InlineData(RequestStatuses.Cancelled, RequestStatus.Cancelled)]
    [InlineData(RequestStatuses.Deferred, RequestStatus.Deferred)]
    public void RequestStatuses_ShouldEqualEnumDbValue(string constant, RequestStatus value) =>
        constant.Should().Be(EnumMapper.ToDbValue(value));

    // --- PlanningModes ↔ PlanningMode enum (DB string == JsonStringEnumMemberName) ---

    [Theory]
    [InlineData(PlanningModes.Leaf, PlanningMode.Leaf)]
    [InlineData(PlanningModes.Summary, PlanningMode.Summary)]
    [InlineData(PlanningModes.Container, PlanningMode.Container)]
    public void PlanningModes_ShouldEqualEnumDbValue(string constant, PlanningMode value) =>
        constant.Should().Be(EnumMapper.ToDbValue(value));

    // --- UserStatusConstants ↔ UserStatus enum (DB string == ParseUserStatus mapping) ---
    // The constants are the canonical users.status DB strings; UserHelper.ParseUserStatus
    // owns the mapping back to the enum, so each constant must parse to its enum member
    // and All must cover exactly the enum members.

    [Theory]
    [InlineData(UserStatusConstants.Active, UserStatus.Active)]
    [InlineData(UserStatusConstants.Disabled, UserStatus.Disabled)]
    [InlineData(UserStatusConstants.PendingVerification, UserStatus.PendingVerification)]
    public void UserStatusConstants_ShouldParseToEnumValue(string constant, UserStatus value) =>
        UserHelper.ParseUserStatus(constant).Should().Be(value);

    [Fact]
    public void UserStatusConstants_All_ShouldCoverExactlyTheEnumMembers() =>
        UserStatusConstants.All.Select(UserHelper.ParseUserStatus)
            .Should().BeEquivalentTo(Enum.GetValues<UserStatus>());

    // --- ConflictKinds (backend ↔ frontend conflicts registry) ---

    [Theory]
    [InlineData(ConflictKinds.ConnectorMismatch, "connector_mismatch")]
    [InlineData(ConflictKinds.Overlap, "overlap")]
    [InlineData(ConflictKinds.CapacityExceeded, "capacity_exceeded")]
    [InlineData(ConflictKinds.StartsInOffTime, "starts_in_off_time")]
    [InlineData(ConflictKinds.SiteMismatch, "site_mismatch")]
    [InlineData(ConflictKinds.BelowMinDuration, "below_min_duration")]
    [InlineData(ConflictKinds.BeforeEarliestStart, "before_earliest_start")]
    [InlineData(ConflictKinds.AfterLatestEnd, "after_latest_end")]
    public void ConflictKinds_ShouldMatchContract(string constant, string expected) =>
        constant.Should().Be(expected);
}

using System.Reflection;
using System.Text.RegularExpressions;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Security;
using Orkyo.Foundation.Tests.Architecture;

namespace Orkyo.Foundation.Tests.Constants;

/// <summary>
/// Drift guards between backend constants and the values they must agree with: the frontend's
/// error codes (read from its source), the enums' DB strings, and the role parser.
/// </summary>
public partial class ConstantContractTests
{
    // --- API error codes (frontend/src/constants/api-error-codes.ts ⊆ backend ApiErrorCodes) ---

    [GeneratedRegex(@"^\s*[A-Z_]+:\s*'([^']+)',", RegexOptions.Multiline)]
    private static partial Regex FrontendCodeRegex();

    /// <summary>
    /// The frontend switches behaviour on these codes; a code no backend constant carries is one
    /// the server never sends, so the branch it guards is dead. Read from the file, not restated.
    /// </summary>
    [Fact]
    public void EveryFrontendErrorCode_IsOneTheBackendSends()
    {
        var dir = TestRepoPaths.FindDirectory("frontend", "src", "constants");
        dir.Should().NotBeNull("could not locate frontend/src/constants");
        var text = File.ReadAllText(Path.Combine(dir!, "api-error-codes.ts"));
        var start = text.IndexOf("export const API_ERROR_CODES = {", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "API_ERROR_CODES must still be declared there");
        var block = text[start..text.IndexOf("} as const;", start, StringComparison.Ordinal)];

        var frontend = FrontendCodeRegex().Matches(block).Select(m => m.Groups[1].Value).ToList();
        frontend.Should().Contain(ApiErrorCodes.SessionExpired, "the extraction must find the codes");

        var backend = new[] { typeof(ApiErrorCodes) }.Concat(typeof(ApiErrorCodes).GetNestedTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        frontend.Where(code => !backend.Contains(code)).Should().BeEmpty(
            "every code in API_ERROR_CODES must be one a backend ApiErrorCodes constant sends");
    }

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

    // --- PredecessorLogic enum ↔ DB string (JsonStringEnumMemberName) ---
    // These strings are the requests_predecessor_logic_check CHECK values (migration 1960),
    // so a drift here is a constraint violation at write time, not just a frontend mismatch.

    [Theory]
    [InlineData("all", PredecessorLogic.All)]
    [InlineData("any", PredecessorLogic.Any)]
    [InlineData("k_of_n", PredecessorLogic.KOfN)]
    public void PredecessorLogic_ShouldEqualCheckConstraintValue(string dbValue, PredecessorLogic value)
    {
        dbValue.Should().Be(EnumMapper.ToDbValue(value));
        // Both directions: "k_of_n" does not match the member name KOfN, so a reader using the
        // naive Enum.Parse path would throw on perfectly valid stored data.
        EnumMapper.FromDbValue<PredecessorLogic>(dbValue).Should().Be(value);
    }

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
}

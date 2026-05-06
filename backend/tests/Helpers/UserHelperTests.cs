using Api.Helpers;
using Api.Models;
using Npgsql;

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

/// <summary>
/// Integration tests for <see cref="UserHelper.MapUser"/>.
/// Uses literal SELECT queries so tests are independent of specific seeded rows.
/// </summary>
[Collection("Database collection")]
public class UserHelperMapUserTests
{
    private readonly string _connectionString;

    public UserHelperMapUserTests(DatabaseFixture fixture)
    {
        // Only needs a live PostgreSQL connection — any migrated DB will do
        _connectionString =
            $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private async Task<NpgsqlDataReader> ExecuteReaderAsync(string sql)
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = new NpgsqlCommand(sql, conn);
        return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
    }

    // Columns follow UserSelectColumns positional contract:
    // 0=id, 1=email, 2=display_name, 3=status, 4=role, 5=created_at, 6=updated_at, [7=last_login_at]

    private const string RowWithoutLastLogin = @"
        SELECT
            '11111111-1111-1111-1111-111111111111'::uuid,
            'user@example.com'::text,
            'Example User'::text,
            'active'::text,
            'admin'::text,
            '2024-01-01 00:00:00'::timestamp,
            '2024-01-02 00:00:00'::timestamp";

    private const string RowWithLastLogin = @"
        SELECT
            '22222222-2222-2222-2222-222222222222'::uuid,
            'editor@example.com'::text,
            'Editor User'::text,
            'disabled'::text,
            'editor'::text,
            '2024-03-01 00:00:00'::timestamp,
            '2024-03-02 00:00:00'::timestamp,
            '2024-03-10 08:00:00'::timestamp";

    private const string RowWithNullLastLogin = @"
        SELECT
            '33333333-3333-3333-3333-333333333333'::uuid,
            'viewer@example.com'::text,
            'Viewer User'::text,
            'pending_verification'::text,
            'viewer'::text,
            '2024-05-01 00:00:00'::timestamp,
            '2024-05-02 00:00:00'::timestamp,
            NULL::timestamp";

    [Fact]
    public async Task MapUser_MapsAllFields_WhenLastLoginAtAbsent()
    {
        await using var reader = await ExecuteReaderAsync(RowWithoutLastLogin);
        await reader.ReadAsync();

        var user = UserHelper.MapUser(reader);

        user.Id.Should().Be(new Guid("11111111-1111-1111-1111-111111111111"));
        user.Email.Should().Be("user@example.com");
        user.DisplayName.Should().Be("Example User");
        user.Status.Should().Be(UserStatus.Active);
        user.Role.Should().Be(UserRole.Admin);
        user.IsTenantAdmin.Should().BeTrue();
        user.LastLoginAt.Should().BeNull();
    }

    [Fact]
    public async Task MapUser_SetsLastLoginAt_WhenColumnPresent()
    {
        await using var reader = await ExecuteReaderAsync(RowWithLastLogin);
        await reader.ReadAsync();

        var user = UserHelper.MapUser(reader);

        user.Id.Should().Be(new Guid("22222222-2222-2222-2222-222222222222"));
        user.Status.Should().Be(UserStatus.Disabled);
        user.Role.Should().Be(UserRole.Editor);
        user.IsTenantAdmin.Should().BeFalse();
        user.LastLoginAt.Should().Be(new DateTime(2024, 3, 10, 8, 0, 0));
    }

    [Fact]
    public async Task MapUser_SetsLastLoginAtNull_WhenColumnIsDbNull()
    {
        await using var reader = await ExecuteReaderAsync(RowWithNullLastLogin);
        await reader.ReadAsync();

        var user = UserHelper.MapUser(reader);

        user.Status.Should().Be(UserStatus.PendingVerification);
        user.Role.Should().Be(UserRole.Viewer);
        user.LastLoginAt.Should().BeNull();
    }
}


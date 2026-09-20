using Api.Constants;
using Api.Models;

namespace Api.Helpers;

/// <summary>
/// Shared utilities for user-related parsing and reader-row mapping.
///
/// Owns the canonical mapping between database string values
/// (<c>users.status</c>, <c>tenant_memberships.role</c>) and the
/// <see cref="UserStatus"/> / <see cref="UserRole"/> enums, plus the
/// projection contract for reader-based user listings (column order +
/// <see cref="MapUser"/>).
/// </summary>
public static class UserHelper
{
    /// <summary>
    /// Parses database user status string to <see cref="UserStatus"/> enum
    /// (case-insensitive). Throws <see cref="ArgumentException"/> on unknown values.
    /// </summary>
    public static UserStatus ParseUserStatus(string dbStatus)
    {
        return dbStatus.ToLowerInvariant() switch
        {
            UserStatusConstants.Active => UserStatus.Active,
            UserStatusConstants.Disabled => UserStatus.Disabled,
            UserStatusConstants.PendingVerification => UserStatus.PendingVerification,
            _ => throw new ArgumentException($"Unknown user status: {dbStatus}")
        };
    }

    /// <summary>
    /// Parses database user role string to <see cref="UserRole"/> enum
    /// (case-insensitive). Throws <see cref="ArgumentException"/> on unknown values.
    /// </summary>
    public static UserRole ParseUserRole(string dbRole)
    {
        return dbRole.ToLowerInvariant() switch
        {
            RoleConstants.Admin => UserRole.Admin,
            RoleConstants.Editor => UserRole.Editor,
            RoleConstants.Viewer => UserRole.Viewer,
            _ => throw new ArgumentException($"Unknown user role: {dbRole}")
        };
    }

    /// <summary>
    /// Maps a database reader row to a <see cref="User"/> object.
    /// Expects columns in order: id, email, display_name, status, role,
    /// created_at, updated_at, last_login_at (optional last column).
    /// <see cref="User.IsTenantAdmin"/> is computed from
    /// <c>role == <see cref="UserRole.Admin"/></c>.
    /// </summary>
    public static User MapUser(Npgsql.NpgsqlDataReader reader)
    {
        var role = ParseUserRole(reader.GetString("role"));
        return new User
        {
            Id = reader.GetGuid("id"),
            Email = reader.GetString("email"),
            DisplayName = reader.GetString("display_name"),
            Status = ParseUserStatus(reader.GetString("status")),
            Role = role,
            IsTenantAdmin = role == UserRole.Admin,
            CreatedAt = reader.GetDateTime("created_at"),
            UpdatedAt = reader.GetDateTime("updated_at"),
            LastLoginAt = reader.GetNullableDateTime("last_login_at")
        };
    }

    /// <summary>
    /// Standard SELECT clause for user queries — and the only column set <see cref="MapUser"/>
    /// accepts. It previously tolerated a reader without <c>last_login_at</c> by checking
    /// <c>FieldCount</c>, a positional assumption in otherwise name-based code, and no caller in
    /// any repo ever passed a short row; only a test did.
    ///
    /// Sources role from
    /// <c>tenant_memberships</c>). Join with:
    /// <c>INNER JOIN tenant_memberships tm ON u.id = tm.user_id WHERE tm.tenant_id = @tenantId</c>.
    /// </summary>
    public const string UserSelectColumns = @"
        u.id, u.email, u.display_name, u.status, tm.role,
        u.created_at, u.updated_at, u.last_login_at";
}

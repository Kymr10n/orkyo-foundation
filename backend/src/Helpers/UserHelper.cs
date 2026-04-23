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
            "active" => UserStatus.Active,
            "disabled" => UserStatus.Disabled,
            "pending_verification" => UserStatus.PendingVerification,
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
        var role = ParseUserRole(reader.GetString(4));
        return new User
        {
            Id = reader.GetGuid(0),
            Email = reader.GetString(1),
            DisplayName = reader.GetString(2),
            Status = ParseUserStatus(reader.GetString(3)),
            Role = role,
            IsTenantAdmin = role == UserRole.Admin,
            CreatedAt = reader.GetDateTime(5),
            UpdatedAt = reader.GetDateTime(6),
            LastLoginAt = reader.FieldCount > 7 && !reader.IsDBNull(7) ? reader.GetDateTime(7) : null
        };
    }

    /// <summary>
    /// Standard SELECT clause for user queries (sources role from
    /// <c>tenant_memberships</c>). Join with:
    /// <c>INNER JOIN tenant_memberships tm ON u.id = tm.user_id WHERE tm.tenant_id = @tenantId</c>.
    /// </summary>
    public const string UserSelectColumns = @"
        u.id, u.email, u.display_name, u.status, tm.role, 
        u.created_at, u.updated_at, u.last_login_at";
}

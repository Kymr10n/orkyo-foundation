using Api.Security;

namespace Api.Constants;

/// <summary>
/// Tenant role string constants for database storage and API communication.
/// These map to the <see cref="TenantRole"/> enum values.
/// </summary>
public static class RoleConstants
{
    public const string Admin = "admin";
    public const string Editor = "editor";
    public const string Viewer = "viewer";
    public const string None = "none";

    /// <summary>
    /// Parses a role string (case-insensitive) into a <see cref="TenantRole"/>.
    /// Unknown or null values map to <see cref="TenantRole.None"/>.
    /// </summary>
    public static TenantRole ParseRoleString(string? roleString)
    {
        return roleString?.ToLowerInvariant() switch
        {
            Admin => TenantRole.Admin,
            Editor => TenantRole.Editor,
            Viewer => TenantRole.Viewer,
            _ => TenantRole.None,
        };
    }

    /// <summary>
    /// Returns true if the supplied string matches one of the known role strings
    /// (admin / editor / viewer). Comparison is case-insensitive.
    /// </summary>
    public static bool IsValidRole(string? role)
    {
        if (string.IsNullOrEmpty(role)) return false;
        var lower = role.ToLowerInvariant();
        return lower == Admin || lower == Editor || lower == Viewer;
    }
}

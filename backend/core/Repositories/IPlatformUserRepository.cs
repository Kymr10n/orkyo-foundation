using Api.Models.Admin;

namespace Api.Repositories;

/// <summary>Minimal user projection for the account lifecycle confirm-activity flow.</summary>
public sealed record AccountLifecycleConfirmRecord(
    Guid UserId,
    string? KeycloakId,
    string DisplayName,
    bool WasDormant);

/// <summary>One row of the admin user list, paired with its Keycloak subject (if linked).</summary>
public sealed record AdminUserListRow(AdminUserSummary Summary, string? KeycloakSub);

/// <summary>Core <c>users</c>-table fields for the admin user detail view.</summary>
public sealed record AdminUserCoreDto(
    Guid Id,
    string Email,
    string? DisplayName,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastLoginAt,
    Guid? OwnedTenantId);

public enum EmailChangeConfirmStatus
{
    /// <summary>Token not found, expired, or the commit update matched zero rows.</summary>
    NotFoundOrExpired,
    /// <summary>Token matched a user with no pending email change recorded.</summary>
    MissingPendingEmail,
    /// <summary>The new email address is already in use by another user.</summary>
    Conflict,
    Confirmed,
}

public sealed record EmailChangeConfirmResult(
    EmailChangeConfirmStatus Status,
    Guid? UserId = null,
    string? PendingEmail = null);

/// <summary>
/// Data access for the control-plane <c>users</c> table. Consolidates the raw ADO.NET that used
/// to live directly in the admin/account/security endpoint handlers.
/// </summary>
public interface IPlatformUserRepository
{
    /// <summary>True when a user with <paramref name="userId"/> exists.</summary>
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Admin user list projection (optionally filtered by email/display-name search or status), capped at 500 rows.</summary>
    Task<List<AdminUserListRow>> GetAdminUserListAsync(string? search, string? status, CancellationToken ct = default);

    /// <summary>Core admin user detail fields (identities/memberships are resolved separately).</summary>
    Task<AdminUserCoreDto?> GetAdminUserCoreAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Current email + display name (falls back to email when display name is unset), or null if the user doesn't exist.</summary>
    Task<(string Email, string DisplayName)?> GetEmailAndDisplayNameAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Records a pending email change (address + confirmation token). Returns false when the
    /// address is already claimed by another user's pending change (unique-index conflict).
    /// </summary>
    Task<bool> SetPendingEmailChangeAsync(Guid userId, string newEmail, string token, CancellationToken ct = default);

    /// <summary>Clears a pending email change (used when confirmation delivery fails).</summary>
    Task ClearPendingEmailChangeAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Atomically confirms a pending email change: validates the token, checks for a conflicting
    /// address, commits the new email, updates the linked Keycloak identity's provider_email, and
    /// invokes <paramref name="updateKeycloakEmailAsync"/> (the Keycloak profile update) before
    /// committing the transaction — a failure there rolls back the whole change.
    /// </summary>
    Task<EmailChangeConfirmResult> ConfirmEmailChangeAsync(
        string token,
        Func<string?, string, string, CancellationToken, Task> updateKeycloakEmailAsync,
        CancellationToken ct = default);

    /// <summary>Finds the user matching an unexpired lifecycle confirm-activity token.</summary>
    Task<AccountLifecycleConfirmRecord?> FindActiveLifecycleConfirmAsync(string token, CancellationToken ct = default);

    /// <summary>Clears lifecycle warning/dormancy state (the user confirmed activity).</summary>
    Task ClearLifecycleStateAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The user's announcement-email opt-out flag, or null if the user doesn't exist.</summary>
    Task<bool?> GetAnnouncementEmailOptOutAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Sets the user's announcement-email opt-out flag.</summary>
    Task SetAnnouncementEmailOptOutAsync(Guid userId, bool optOut, CancellationToken ct = default);

    /// <summary>Opts a user out of announcement emails by their unsubscribe token. Returns true if a row matched.</summary>
    Task<bool> SetAnnouncementOptOutByTokenAsync(Guid unsubscribeToken, CancellationToken ct = default);

    /// <summary>The user's tenant memberships (tenant + role + status), ordered by tenant display name.</summary>
    Task<List<AdminUserMembership>> GetMembershipsAsync(Guid userId, CancellationToken ct = default);
}

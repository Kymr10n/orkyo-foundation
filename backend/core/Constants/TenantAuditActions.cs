namespace Api.Constants;

/// <summary>
/// Tenant-scoped audit action names (controlled vocabulary, "&lt;target&gt;.&lt;verb&gt;") written
/// into the tenant's own <c>audit_events</c> by tenant-context operations via
/// <see cref="Api.Services.ITenantUserService.RecordAuditEventAsync"/>. Covers the user/invitation
/// lifecycle (<c>InvitationService</c>/<c>UserManagementService</c>), sites, and tenant settings.
/// Security-lifecycle actions (sign-in, break-glass) live in <see cref="SecurityAuditActions"/>.
/// </summary>
public static class TenantAuditActions
{
    // User / invitation lifecycle (emitted by InvitationService / UserManagementService).
    public const string UserInvited = "user.invited";
    public const string UserAddedToTenant = "user.added_to_tenant";
    public const string UserInvitationAccepted = "user.invitation_accepted";
    public const string UserRoleUpdated = "user.role_updated";
    public const string UserRemovedFromTenant = "user.removed_from_tenant";
    public const string InvitationRevoked = "invitation.revoked";
    public const string InvitationResent = "invitation.resent";

    public const string SiteCreated = "site.created";
    public const string SiteUpdated = "site.updated";
    public const string SiteDeleted = "site.deleted";

    public const string SettingsUpdated = "settings.updated";
    public const string SettingsReset = "settings.reset";

    // AI assistant administration. The credential events never carry the key or its
    // ciphertext — only that a key was set, replaced, probed, or removed.
    public const string AiCredentialSaved = "ai.credential_saved";
    public const string AiCredentialDeleted = "ai.credential_deleted";
    public const string AiCredentialTested = "ai.credential_tested";
    public const string AiAllowanceGranted = "ai.allowance_granted";
    public const string AiAllowanceRevoked = "ai.allowance_revoked";
}

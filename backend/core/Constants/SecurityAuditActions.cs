namespace Api.Constants;

/// <summary>
/// Security-lifecycle audit action names (controlled vocabulary, "&lt;target&gt;.&lt;verb&gt;").
/// Recorded via <see cref="Api.Services.IAdminAuditService"/> and read by the tenant audit log.
/// Shared by foundation (BFF sign-in) and the products (SaaS break-glass endpoints). SaaS keeps
/// its platform-admin actions (tenant.*, membership.*, quota.*) in its own <c>Api.Constants.AuditEvents</c>.
/// </summary>
public static class SecurityAuditActions
{
    public const string SessionSignedIn = "session.signed_in";
    public const string SessionSignedOut = "session.signed_out";

    public const string BreakGlassGranted = "break_glass.granted";
    public const string BreakGlassRevoked = "break_glass.revoked";
    public const string BreakGlassRenewed = "break_glass.renewed";
}

namespace Orkyo.Shared;

/// <summary>
/// Tenant lifecycle status values as stored in <c>tenants.status</c>.
/// Mirrors the DB check constraint; treat values as authoritative string keys.
/// </summary>
public static class TenantStatusConstants
{
    public const string Active = "active";
    public const string Suspended = "suspended";
    public const string Pending = "pending";
    public const string Deleted = "deleted";
    public const string Deleting = "deleting";
}

/// <summary>
/// Canonical suspension-reason values. Must match the DB check constraint on
/// <c>tenants.suspension_reason</c> (migration V015).
/// </summary>
public static class SuspensionReasonConstants
{
    public const string Inactivity = "inactivity";
    public const string ManualAdmin = "manual_admin";
    public const string PaymentOverdue = "payment_overdue";
    public const string SecurityIncident = "security_incident";
    public const string TrialExpired = "trial_expired";
    public const string ComplianceHold = "compliance_hold";
}

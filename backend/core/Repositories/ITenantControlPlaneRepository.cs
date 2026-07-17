namespace Api.Repositories;

/// <summary>Core control-plane <c>tenants</c> row projection shared by the repository's reads and writes.</summary>
public sealed record TenantRecord(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string DbIdentifier,
    Guid? OwnerUserId,
    DateTime CreatedAt);

/// <summary>Owner + status of a tenant (the inputs of the owner-gated decision policies).</summary>
public sealed record TenantOwnerStatus(Guid? OwnerUserId, string Status);

/// <summary>Role + status of a single tenant membership.</summary>
public sealed record TenantMembershipRoleStatus(string Role, string Status);

/// <summary>One row of a user's tenant-membership list (joined with the tenant record).</summary>
public sealed record TenantMembershipRow(
    Guid TenantId,
    string TenantSlug,
    string TenantDisplayName,
    string TenantStatus,
    Guid? OwnerUserId,
    string Role,
    string MembershipStatus,
    DateTime JoinedAt);

/// <summary>
/// Inputs of <c>TenantLeaveMembershipPolicy</c>, loaded in one round trip:
/// the tenant's owner, its active-admin count, and the leaving user's active role
/// (null when the user has no active membership).
/// </summary>
public sealed record TenantLeaveLookup(Guid? OwnerUserId, long ActiveAdminCount, string? Role);

/// <summary>
/// Data access for the control-plane <c>tenants</c> / <c>tenant_memberships</c> tables.
/// Replaces the per-query <c>*QueryContract</c> / <c>*CommandFactory</c> / <c>*Flow</c>
/// triplets (removed in 0.8.0). Pure data access — the decision logic stays in the
/// <c>Tenant*Policy</c> classes, which callers feed from these lookups.
/// </summary>
public interface ITenantControlPlaneRepository
{
    /// <summary>True when <paramref name="userId"/> owns a tenant that is not being deleted (one-tenant-per-user policy input).</summary>
    Task<bool> OwnsActiveTenantAsync(Guid userId, CancellationToken ct = default);

    /// <summary>True when a tenant with <paramref name="slug"/> already exists.</summary>
    Task<bool> IsSlugTakenAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Creates the tenant row (status <c>active</c>) and its owner admin membership in one
    /// transaction, returning the created record. Constraint violations (e.g. duplicate slug)
    /// propagate as <see cref="Npgsql.PostgresException"/> and roll back both inserts.
    /// </summary>
    Task<TenantRecord> CreateTenantWithOwnerAsync(
        string slug, string displayName, string dbIdentifier, Guid ownerId, CancellationToken ct = default);

    /// <summary>The tenant record, or null when it doesn't exist.</summary>
    Task<TenantRecord?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>The tenant record, or null when no tenant has <paramref name="slug"/>.</summary>
    Task<TenantRecord?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Updates the display name and returns the updated record in the same round trip, or null when the tenant doesn't exist.</summary>
    Task<TenantRecord?> UpdateDisplayNameAsync(Guid tenantId, string displayName, CancellationToken ct = default);

    /// <summary>Owner + status for the owner-gated policies, or null when the tenant doesn't exist.</summary>
    Task<TenantOwnerStatus?> GetOwnerStatusAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Role + status of the user's membership in the tenant (any status), or null when no membership row exists.</summary>
    Task<TenantMembershipRoleStatus?> GetMembershipRoleStatusAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>All memberships of a user with their tenant records, newest membership first.</summary>
    Task<List<TenantMembershipRow>> GetUserMembershipsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The <c>TenantLeaveMembershipPolicy</c> inputs for one user leaving one tenant, in a single round trip.</summary>
    Task<TenantLeaveLookup> GetLeaveLookupAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    /// <summary>Soft-deletes the tenant: status → <c>deleting</c>, bumping <c>updated_at</c> (the purge grace clock).</summary>
    Task MarkDeletingAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Cancels a pending deletion: status → <c>active</c>.</summary>
    Task MarkActiveAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Sets the tenant's owner to <paramref name="newOwnerId"/> (eligibility is the caller's policy check).</summary>
    Task TransferOwnershipAsync(Guid tenantId, Guid newOwnerId, CancellationToken ct = default);

    /// <summary>Deletes the user's membership row in the tenant.</summary>
    Task DeleteMembershipAsync(Guid tenantId, Guid userId, CancellationToken ct = default);
}

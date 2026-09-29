using Api.Repositories;
using Api.Security;

namespace Api.Services;

/// <summary>
/// Default <see cref="IAdminAuditService"/> that writes to <c>control_plane.audit_events</c>.
/// Failures are logged but don't break the calling operation — audit logging is best-effort.
/// The row itself is written by <see cref="AuditEventWriter"/>, shared with the tenant-side writer.
/// </summary>
public sealed class AdminAuditService : IAdminAuditService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<AdminAuditService> _logger;

    public AdminAuditService(IDbConnectionFactory connectionFactory, ICurrentTenant currentTenant, ILogger<AdminAuditService> logger)
    {
        _connectionFactory = connectionFactory;
        _currentTenant = currentTenant;
        _logger = logger;
    }

    public async Task RecordEventAsync(
        Guid? actorUserId,
        string action,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null, CancellationToken ct = default)
    {
        // Capture the tenant synchronously (before the first await) so fire-and-forget callers
        // still stamp the right tenant even after the request scope unwinds. Events with no
        // resolved tenant (platform/site-admin, SkipTenantResolution) stay NULL.
        Guid? tenantId = _currentTenant.HasTenant ? _currentTenant.TenantId : null;
        try
        {
            await using var conn = _connectionFactory.CreateControlPlaneConnection();
            await conn.OpenAsync(ct);

            await AuditEventWriter.InsertControlPlaneAsync(
                conn, tenantId, actorUserId, action, targetType, targetId, metadata, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit event: {Action}", action);
        }
    }
}

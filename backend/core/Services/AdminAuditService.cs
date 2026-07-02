using Api.Security;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Default <see cref="IAdminAuditService"/> that writes to <c>control_plane.audit_events</c>
/// via the shared <see cref="ControlPlaneAuditEventCommandFactory"/>. Failures are logged but
/// don't break the calling operation — audit logging is best-effort. The table-not-found path
/// lets foundation-only deployments that haven't yet applied the audit migration still operate.
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

            using var cmd = ControlPlaneAuditEventCommandFactory.CreateInsertAuditEventCommand(
                conn, action, actorUserId, targetType, targetId, metadata, tenantId);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            _logger.LogWarning("audit_events table does not exist — skipping audit");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record audit event: {Action}", action);
        }
    }
}

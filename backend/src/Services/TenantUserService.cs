using Npgsql;

namespace Api.Services;

/// <summary>
/// Service for tenant-local user operations.
/// Handles user stub creation and audit logging in tenant databases.
/// </summary>
public interface ITenantUserService
{
    /// <summary>
    /// Creates a minimal user stub in the org database for FK relationships.
    /// The full user record exists in control_plane, but org databases need a stub for references.
    /// </summary>
    Task CreateUserStubInTenantDatabaseAsync(OrgContext org, Guid userId, string email);

    /// <summary>
    /// Records an audit event in the org's audit log.
    /// </summary>
    Task RecordAuditEventAsync(
        OrgContext org,
        string action,
        Guid? actorUserId = null,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null);
}

public class TenantUserService : ITenantUserService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<TenantUserService> _logger;

    public TenantUserService(
        IDbConnectionFactory connectionFactory,
        ILogger<TenantUserService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task CreateUserStubInTenantDatabaseAsync(OrgContext org, Guid userId, string email)
    {
        try
        {
            await using var conn = _connectionFactory.CreateOrgConnection(org);
            await conn.OpenAsync();

            await using var cmd = TenantUserStubCommandFactory.CreateInsertUserStubCommand(
                conn, userId, email.ToLowerInvariant());

            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create user stub in tenant database for user {UserId}", userId);
            // Don't fail operations if tenant stub creation fails
        }
    }

    public async Task RecordAuditEventAsync(
        OrgContext org,
        string action,
        Guid? actorUserId = null,
        string? targetType = null,
        string? targetId = null,
        object? metadata = null)
    {
        try
        {
            await using var conn = _connectionFactory.CreateOrgConnection(org);
            await conn.OpenAsync();
            await using var transaction = await conn.BeginTransactionAsync();

            var cmd = TenantAuditEventCommandFactory.CreateInsertAuditEventCommand(
                conn, transaction, action, actorUserId, targetType, targetId, metadata);

            await cmd.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") // Table does not exist
        {
            // Audit events table not yet created - silently skip
            _logger.LogWarning("Audit events table does not exist - skipping audit logging");
        }
    }
}

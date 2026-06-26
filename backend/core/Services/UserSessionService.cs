using Api.Security;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Captured device/IP metadata for one Keycloak session (joined by sid).
/// </summary>
public sealed record UserSessionRow
{
    public required string KeycloakSessionId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? Browser { get; init; }
    public string? OperatingSystem { get; init; }
    public string? DeviceType { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastSeenAt { get; init; }
}

/// <summary>
/// App-side store of per-device session metadata captured at BFF login, keyed by
/// the Keycloak <c>sid</c>. Lets the "Active Sessions" UI show the real browser/OS
/// and client IP instead of the BFF client name + container IP that Keycloak records.
/// </summary>
public interface IUserSessionService
{
    /// <summary>Insert-or-update the device row for a Keycloak session (parses the User-Agent).</summary>
    Task UpsertAsync(Guid userId, string keycloakSessionId, string? ipAddress, string? userAgent, CancellationToken ct = default);

    /// <summary>All captured device rows for a user, keyed for joining to the live Keycloak list.</summary>
    Task<IReadOnlyList<UserSessionRow>> GetByUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Delete a user's rows whose Keycloak session is no longer live (GDPR prune).</summary>
    Task PruneExceptAsync(Guid userId, IReadOnlyCollection<string> liveSessionIds, CancellationToken ct = default);

    /// <summary>Delete the row for a single revoked Keycloak session.</summary>
    Task RemoveAsync(string keycloakSessionId, CancellationToken ct = default);

    /// <summary>Delete all of a user's rows (on logout-all).</summary>
    Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default);
}

public sealed class UserSessionService : IUserSessionService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<UserSessionService> _logger;

    public UserSessionService(IDbConnectionFactory connectionFactory, ILogger<UserSessionService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task UpsertAsync(Guid userId, string keycloakSessionId, string? ipAddress, string? userAgent, CancellationToken ct = default)
    {
        var parsed = UserAgentParser.Parse(userAgent);

        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO user_sessions
                (user_id, keycloak_session_id, ip_address, user_agent, browser, operating_system, device_type, created_at, last_seen_at)
            VALUES (@userId, @sid, @ip, @ua, @browser, @os, @deviceType, NOW(), NOW())
            ON CONFLICT (keycloak_session_id) DO UPDATE SET
                ip_address       = EXCLUDED.ip_address,
                user_agent       = EXCLUDED.user_agent,
                browser          = EXCLUDED.browser,
                operating_system = EXCLUDED.operating_system,
                device_type      = EXCLUDED.device_type,
                last_seen_at     = NOW()
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("sid", keycloakSessionId);
        cmd.Parameters.AddWithValue("ip", (object?)ipAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("ua", (object?)userAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("browser", (object?)parsed.Browser ?? DBNull.Value);
        cmd.Parameters.AddWithValue("os", (object?)parsed.OperatingSystem ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deviceType", parsed.DeviceType);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<UserSessionRow>> GetByUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT keycloak_session_id, ip_address, user_agent, browser, operating_system, device_type, created_at, last_seen_at
            FROM user_sessions
            WHERE user_id = @userId
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);

        var rows = new List<UserSessionRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new UserSessionRow
            {
                KeycloakSessionId = reader.GetString(0),
                IpAddress = reader.IsDBNull(1) ? null : reader.GetString(1),
                UserAgent = reader.IsDBNull(2) ? null : reader.GetString(2),
                Browser = reader.IsDBNull(3) ? null : reader.GetString(3),
                OperatingSystem = reader.IsDBNull(4) ? null : reader.GetString(4),
                DeviceType = reader.IsDBNull(5) ? null : reader.GetString(5),
                CreatedAt = reader.GetDateTime(6),
                LastSeenAt = reader.GetDateTime(7),
            });
        }
        return rows;
    }

    public async Task PruneExceptAsync(Guid userId, IReadOnlyCollection<string> liveSessionIds, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        // NOT (... = ANY(@ids)) with an empty array is always true → removes every
        // row for the user (correct when the user has no live sessions left).
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM user_sessions
            WHERE user_id = @userId AND NOT (keycloak_session_id = ANY(@ids))
        ", db);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("ids", liveSessionIds.ToArray());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAsync(string keycloakSessionId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_sessions WHERE keycloak_session_id = @sid", db);
        cmd.Parameters.AddWithValue("sid", keycloakSessionId);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = _connectionFactory.CreateControlPlaneConnection();
        await db.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_sessions WHERE user_id = @userId", db);
        cmd.Parameters.AddWithValue("userId", userId);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}

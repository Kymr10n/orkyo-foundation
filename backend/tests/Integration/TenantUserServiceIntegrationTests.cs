using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed integration tests for the foundation-owned <see cref="TenantUserService"/>
/// using an in-foundation test connection factory. Proves that the
/// SaaS↔Foundation split composes correctly end-to-end: foundation service + SaaS
/// connection factory + foundation migrations + foundation query contracts all
/// operate on the same real Postgres schema.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantUserServiceIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public TenantUserServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUserStub_ShouldInsertRow_InTenantUsersTable()
    {
        var service = BuildService();
        var org = BuildOrgContextForTestTenant();
        var userId = Guid.NewGuid();
        var email = "Alice@Example.COM";

        await service.CreateUserStubInTenantDatabaseAsync(org, userId, email);

        var persisted = await ReadUserStubAsync(userId);
        persisted.Should().NotBeNull();
        persisted!.Value.email.Should().Be("alice@example.com", "email is normalized to lowercase before insert");
    }

    [Fact]
    public async Task CreateUserStub_ShouldBeIdempotent_WhenCalledTwice()
    {
        var service = BuildService();
        var org = BuildOrgContextForTestTenant();
        var userId = Guid.NewGuid();

        await service.CreateUserStubInTenantDatabaseAsync(org, userId, "bob@example.com");
        await service.CreateUserStubInTenantDatabaseAsync(org, userId, "bob@example.com");

        var count = await CountUsersByIdAsync(userId);
        count.Should().Be(1, "INSERT ... ON CONFLICT DO NOTHING prevents duplicates");
    }

    [Fact]
    public async Task RecordAuditEvent_ShouldInsertRow_IntoAuditEventsTable()
    {
        var service = BuildService();
        var org = BuildOrgContextForTestTenant();
        var actorUserId = Guid.NewGuid();
        await service.CreateUserStubInTenantDatabaseAsync(org, actorUserId, "auditor@example.com");

        await service.RecordAuditEventAsync(
            org,
            action: "test.action",
            actorUserId: actorUserId,
            targetType: "tenant",
            targetId: "abc-123",
            metadata: new { key = "value", count = 7 });

        var recorded = await ReadLatestAuditEventForActorAsync(actorUserId);
        recorded.Should().NotBeNull();
        recorded!.Value.action.Should().Be("test.action");
        recorded.Value.actorType.Should().Be("user", "audit actor_type maps to 'user' when an actor_user_id is supplied");
        recorded.Value.targetType.Should().Be("tenant");
        recorded.Value.targetId.Should().Be("abc-123");
        recorded.Value.metadata.Should().Contain("\"key\"").And.Contain("\"value\"");
        recorded.Value.metadata.Should().Contain("\"count\"").And.Contain("7");
    }

    [Fact]
    public async Task RecordAuditEvent_ShouldMapActorTypeToSystem_WhenNoActorSupplied()
    {
        var service = BuildService();
        var org = BuildOrgContextForTestTenant();
        var uniqueAction = $"system.bootstrap.{Guid.NewGuid():N}";

        await service.RecordAuditEventAsync(org, action: uniqueAction);

        var recorded = await ReadAuditEventByActionAsync(uniqueAction);
        recorded.Should().NotBeNull();
        recorded!.Value.actorType.Should().Be("system");
        recorded.Value.actorUserId.Should().BeNull();
    }

    // ── composition ──────────────────────────────────────────────────────────

    private TenantUserService BuildService()
    {
        var factory = _fixture.CreateConnectionFactory();
        return new TenantUserService(factory, NullLogger<TenantUserService>.Instance);
    }


    private OrgContext BuildOrgContextForTestTenant() => new()
    {
        OrgId = Guid.NewGuid(),
        OrgSlug = "test-tenant",
        DbConnectionString = _fixture.TestTenantConnectionString,
    };

    // ── direct DB readers (bypass the service under test) ────────────────────

    private async Task<(string email, DateTime createdAt)?> ReadUserStubAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT email, created_at FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.GetDateTime(1));
    }

    private async Task<long> CountUsersByIdAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<(string action, string actorType, Guid? actorUserId, string? targetType, string? targetId, string metadata)?>
        ReadLatestAuditEventForActorAsync(Guid actorUserId)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT action, actor_type, actor_user_id, target_type, target_id, metadata::text " +
            "FROM audit_events WHERE actor_user_id = @actor ORDER BY created_at DESC LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("actor", actorUserId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5));
    }

    private async Task<(string actorType, Guid? actorUserId)?> ReadAuditEventByActionAsync(string action)
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT actor_type, actor_user_id FROM audit_events WHERE action = @a LIMIT 1", conn);
        cmd.Parameters.AddWithValue("a", action);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }
}

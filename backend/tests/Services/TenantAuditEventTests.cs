using Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Tests.Services;

public class TenantAuditEventQueryContractTests
{
    [Fact]
    public void BuildInsertAuditEventSql_TargetsAuditEventsWithExpectedColumns()
    {
        var sql = TenantAuditEventQueryContract.BuildInsertAuditEventSql();

        sql.Should().Contain("INSERT INTO audit_events (actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)");
        sql.Should().Contain("VALUES (@actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())");
    }

    [Fact]
    public void ResolveActorType_WithUserId_ReturnsUser()
    {
        TenantAuditEventQueryContract.ResolveActorType(Guid.NewGuid())
            .Should().Be(TenantAuditEventQueryContract.ActorTypeUser);
    }

    [Fact]
    public void ResolveActorType_WithoutUserId_ReturnsSystem()
    {
        TenantAuditEventQueryContract.ResolveActorType(null)
            .Should().Be(TenantAuditEventQueryContract.ActorTypeSystem);
    }

    [Fact]
    public void ActorTypeConstants_AreStable()
    {
        TenantAuditEventQueryContract.ActorTypeUser.Should().Be("user");
        TenantAuditEventQueryContract.ActorTypeSystem.Should().Be("system");
    }
}

public class TenantAuditEventCommandFactoryTests
{
    [Fact]
    public void CreateInsertAuditEventCommand_WithUserActor_BindsAllParametersAndUserActorType()
    {
        using var connection = new NpgsqlConnection();
        var actorId = Guid.NewGuid();

        using var command = TenantAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection, transaction: null,
            action: "user.created",
            actorUserId: actorId,
            targetType: "user",
            targetId: "abc-123",
            metadata: new { foo = "bar" });

        command.CommandText.Should().Be(TenantAuditEventQueryContract.BuildInsertAuditEventSql());
        command.Parameters[TenantAuditEventQueryContract.ActorUserIdParameterName].Value.Should().Be(actorId);
        command.Parameters[TenantAuditEventQueryContract.ActorTypeParameterName].Value.Should().Be("user");
        command.Parameters[TenantAuditEventQueryContract.ActionParameterName].Value.Should().Be("user.created");
        command.Parameters[TenantAuditEventQueryContract.TargetTypeParameterName].Value.Should().Be("user");
        command.Parameters[TenantAuditEventQueryContract.TargetIdParameterName].Value.Should().Be("abc-123");

        var metadataParam = command.Parameters[TenantAuditEventQueryContract.MetadataParameterName];
        metadataParam.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        metadataParam.Value.Should().Be("{\"foo\":\"bar\"}");
    }

    [Fact]
    public void CreateInsertAuditEventCommand_WithoutActorOrMetadata_BindsSystemActorAndDbNulls()
    {
        using var connection = new NpgsqlConnection();

        using var command = TenantAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection, transaction: null,
            action: "system.heartbeat",
            actorUserId: null,
            targetType: null,
            targetId: null,
            metadata: null);

        command.Parameters[TenantAuditEventQueryContract.ActorUserIdParameterName].Value.Should().Be(DBNull.Value);
        command.Parameters[TenantAuditEventQueryContract.ActorTypeParameterName].Value.Should().Be("system");
        command.Parameters[TenantAuditEventQueryContract.TargetTypeParameterName].Value.Should().Be(DBNull.Value);
        command.Parameters[TenantAuditEventQueryContract.TargetIdParameterName].Value.Should().Be(DBNull.Value);

        var metadataParam = command.Parameters[TenantAuditEventQueryContract.MetadataParameterName];
        metadataParam.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        metadataParam.Value.Should().Be(DBNull.Value);
    }
}

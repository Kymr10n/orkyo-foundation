using Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Orkyo.Foundation.Tests.Services;

public class ControlPlaneAuditEventQueryContractTests
{
    [Fact]
    public void BuildInsertAuditEventSql_TargetsAuditEventsWithIdColumn()
    {
        var sql = ControlPlaneAuditEventQueryContract.BuildInsertAuditEventSql();

        sql.Should().Contain("INSERT INTO audit_events (id, actor_user_id, actor_type, action, target_type, target_id, metadata, created_at)");
        sql.Should().Contain("VALUES (@id, @actorUserId, @actorType, @action, @targetType, @targetId, @metadata, NOW())");
    }

    [Fact]
    public void ResolveActorType_DelegatesToTenantAuditSemantics()
    {
        ControlPlaneAuditEventQueryContract.ResolveActorType(Guid.NewGuid())
            .Should().Be(ControlPlaneAuditEventQueryContract.ActorTypeUser).And.Be("user");
        ControlPlaneAuditEventQueryContract.ResolveActorType(null)
            .Should().Be(ControlPlaneAuditEventQueryContract.ActorTypeSystem).And.Be("system");
    }
}

public class ControlPlaneAuditEventCommandFactoryTests
{
    [Fact]
    public void CreateInsertAuditEventCommand_WithUserActor_BindsAllParametersIncludingGeneratedId()
    {
        using var connection = new NpgsqlConnection();
        var actorId = Guid.NewGuid();

        using var command = ControlPlaneAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection,
            action: "tenant.created",
            actorUserId: actorId,
            targetType: "tenant",
            targetId: "abc-tenant",
            metadata: new { foo = "bar" });

        command.CommandText.Should().Be(ControlPlaneAuditEventQueryContract.BuildInsertAuditEventSql());

        command.Parameters[ControlPlaneAuditEventQueryContract.IdParameterName].Value
            .Should().BeOfType<Guid>().Which.Should().NotBe(Guid.Empty);
        command.Parameters[ControlPlaneAuditEventQueryContract.ActorUserIdParameterName].Value.Should().Be(actorId);
        command.Parameters[ControlPlaneAuditEventQueryContract.ActorTypeParameterName].Value.Should().Be("user");
        command.Parameters[ControlPlaneAuditEventQueryContract.ActionParameterName].Value.Should().Be("tenant.created");
        command.Parameters[ControlPlaneAuditEventQueryContract.TargetTypeParameterName].Value.Should().Be("tenant");
        command.Parameters[ControlPlaneAuditEventQueryContract.TargetIdParameterName].Value.Should().Be("abc-tenant");

        var metadataParam = command.Parameters[ControlPlaneAuditEventQueryContract.MetadataParameterName];
        metadataParam.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        metadataParam.Value.Should().Be("{\"foo\":\"bar\"}");
    }

    [Fact]
    public void CreateInsertAuditEventCommand_WithoutActorOrMetadata_BindsSystemActorAndDbNulls()
    {
        using var connection = new NpgsqlConnection();

        using var command = ControlPlaneAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection,
            action: "system.boot",
            actorUserId: null,
            targetType: null,
            targetId: null,
            metadata: null);

        command.Parameters[ControlPlaneAuditEventQueryContract.ActorUserIdParameterName].Value.Should().Be(DBNull.Value);
        command.Parameters[ControlPlaneAuditEventQueryContract.ActorTypeParameterName].Value.Should().Be("system");
        command.Parameters[ControlPlaneAuditEventQueryContract.TargetTypeParameterName].Value.Should().Be(DBNull.Value);
        command.Parameters[ControlPlaneAuditEventQueryContract.TargetIdParameterName].Value.Should().Be(DBNull.Value);

        var metadataParam = command.Parameters[ControlPlaneAuditEventQueryContract.MetadataParameterName];
        metadataParam.NpgsqlDbType.Should().Be(NpgsqlDbType.Jsonb);
        metadataParam.Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void CreateInsertAuditEventCommand_GeneratesUniqueIdsAcrossInvocations()
    {
        using var connection = new NpgsqlConnection();

        using var first = ControlPlaneAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection, "x", null, null, null, null);
        using var second = ControlPlaneAuditEventCommandFactory.CreateInsertAuditEventCommand(
            connection, "x", null, null, null, null);

        first.Parameters[ControlPlaneAuditEventQueryContract.IdParameterName].Value
            .Should().NotBe(second.Parameters[ControlPlaneAuditEventQueryContract.IdParameterName].Value);
    }
}

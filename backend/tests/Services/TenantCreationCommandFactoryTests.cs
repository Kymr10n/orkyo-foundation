using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantCreationCommandFactoryTests
{
    [Fact]
    public void CreateInsertTenantCommand_ShouldBindAllParameters()
    {
        using var connection = new NpgsqlConnection();
        var ownerId = Guid.NewGuid();

        using var command = TenantCreationCommandFactory.CreateInsertTenantCommand(
            connection,
            transaction: null,
            slug: "acme",
            displayName: "Acme",
            dbIdentifier: "tenant_acme",
            ownerId: ownerId);

        command.CommandText.Should().Be(TenantCreationQueryContract.BuildInsertTenantSql());
        command.Parameters[TenantCreationQueryContract.SlugParameterName].Value.Should().Be("acme");
        command.Parameters[TenantCreationQueryContract.DisplayNameParameterName].Value.Should().Be("Acme");
        command.Parameters[TenantCreationQueryContract.DbIdentifierParameterName].Value.Should().Be("tenant_acme");
        command.Parameters[TenantCreationQueryContract.OwnerIdParameterName].Value.Should().Be(ownerId);
    }

    [Fact]
    public void CreateInsertOwnerMembershipCommand_ShouldBindUserAndTenantIds()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        using var command = TenantCreationCommandFactory.CreateInsertOwnerMembershipCommand(connection, null, userId, tenantId);

        command.CommandText.Should().Be(TenantCreationQueryContract.BuildInsertOwnerMembershipSql());
        command.Parameters[TenantCreationQueryContract.UserIdParameterName].Value.Should().Be(userId);
        command.Parameters[TenantCreationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateSelectUserEmailByIdCommand_ShouldBindUserId()
    {
        using var connection = new NpgsqlConnection();
        var userId = Guid.NewGuid();

        using var command = TenantCreationCommandFactory.CreateSelectUserEmailByIdCommand(connection, userId);

        command.CommandText.Should().Be(TenantCreationQueryContract.BuildSelectUserEmailByIdSql());
        command.Parameters[TenantCreationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }
}

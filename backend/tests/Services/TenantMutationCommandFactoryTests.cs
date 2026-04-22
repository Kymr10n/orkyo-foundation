using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Services;

public class TenantMutationCommandFactoryTests
{
    [Fact]
    public void CreateMarkDeletingCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateMarkDeletingCommand(connection, tenantId);
        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateMarkActiveCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateMarkActiveCommand(connection, tenantId);
        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateTouchLastActivityCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateTouchLastActivityCommand(connection, tenantId);
        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateUpdateDisplayNameCommand_ShouldBindDisplayNameAndTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateUpdateDisplayNameCommand(connection, tenantId, "Acme");

        command.Parameters[TenantMutationQueryContract.DisplayNameParameterName].Value.Should().Be("Acme");
        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }

    [Fact]
    public void CreateTransferOwnershipCommand_ShouldBindTenantIdAndNewOwnerId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateTransferOwnershipCommand(connection, tenantId, newOwnerId);

        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMutationQueryContract.NewOwnerIdParameterName].Value.Should().Be(newOwnerId);
    }

    [Fact]
    public void CreateDeleteMembershipCommand_ShouldBindTenantIdAndUserId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateDeleteMembershipCommand(connection, tenantId, userId);

        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
        command.Parameters[TenantMutationQueryContract.UserIdParameterName].Value.Should().Be(userId);
    }

    [Fact]
    public void CreateReactivateSuspendedTenantCommand_ShouldBindTenantId()
    {
        using var connection = new NpgsqlConnection();
        var tenantId = Guid.NewGuid();
        using var command = TenantMutationCommandFactory.CreateReactivateSuspendedTenantCommand(connection, tenantId);

        command.Parameters[TenantMutationQueryContract.TenantIdParameterName].Value.Should().Be(tenantId);
    }
}

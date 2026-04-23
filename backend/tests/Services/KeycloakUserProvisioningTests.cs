using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class KeycloakUserProvisioningQueryContractTests
{
    [Fact]
    public void BuildSelectUserByLowerEmailSql_ProjectsIdEmailDisplayNameStatus_AndIsCaseInsensitive()
    {
        var sql = KeycloakUserProvisioningQueryContract.BuildSelectUserByLowerEmailSql();

        sql.Should().Contain("SELECT id, email, display_name, status");
        sql.Should().Contain("FROM users");
        // Drift guard: case-insensitive match is required to honor invitation flows where
        // the inviter's email casing may differ from the Keycloak token email casing.
        sql.Should().Contain("WHERE LOWER(email) = LOWER(@email)");
    }

    [Fact]
    public void BuildInsertNewActiveUserSql_BindsAllUserColumnsAndStampsActiveStatusAndTimestamps()
    {
        var sql = KeycloakUserProvisioningQueryContract.BuildInsertNewActiveUserSql();

        sql.Should().Contain("INSERT INTO users (id, email, display_name, status, last_login_at, created_at, updated_at)");
        sql.Should().Contain("VALUES (@id, @email, @displayName, 'active', NOW(), NOW(), NOW())");
    }

    [Fact]
    public void BuildInsertIdentityLinkWithIdSql_InsertsExplicitIdKeycloakLinkRow()
    {
        var sql = KeycloakUserProvisioningQueryContract.BuildInsertIdentityLinkWithIdSql();

        sql.Should().Contain("INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email, created_at)");
        sql.Should().Contain("VALUES (@id, @userId, 'keycloak', @subject, @email, NOW())");
        // Drift guard: this is the transactional first-time-link variant, no ON CONFLICT.
        sql.Should().NotContain("ON CONFLICT");
    }

    [Fact]
    public void Constants_AreStable()
    {
        KeycloakUserProvisioningQueryContract.EmailParameterName.Should().Be("email");
        KeycloakUserProvisioningQueryContract.IdParameterName.Should().Be("id");
        KeycloakUserProvisioningQueryContract.UserIdParameterName.Should().Be("userId");
        KeycloakUserProvisioningQueryContract.DisplayNameParameterName.Should().Be("displayName");
        KeycloakUserProvisioningQueryContract.SubjectParameterName.Should().Be("subject");
        KeycloakUserProvisioningQueryContract.ActiveUserStatus.Should().Be("active");
    }
}

public class KeycloakUserProvisioningCommandFactoryTests
{
    [Fact]
    public void CreateSelectUserByLowerEmailCommand_BindsEmailParameter()
    {
        using var cmd = KeycloakUserProvisioningCommandFactory.CreateSelectUserByLowerEmailCommand(
            connection: null!, email: "User@Example.com");

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("email");
        cmd.Parameters[0].Value.Should().Be("User@Example.com");
    }

    [Fact]
    public void CreateInsertNewActiveUserCommand_BindsIdEmailDisplayNameParameters()
    {
        var userId = Guid.NewGuid();

        using var cmd = KeycloakUserProvisioningCommandFactory.CreateInsertNewActiveUserCommand(
            connection: null!, transaction: null, userId, email: "user@example.com", displayName: "User One");

        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters["id"].Value.Should().Be(userId);
        cmd.Parameters["email"].Value.Should().Be("user@example.com");
        cmd.Parameters["displayName"].Value.Should().Be("User One");
    }

    [Fact]
    public void CreateInsertIdentityLinkWithIdCommand_BindsAllFourParameters()
    {
        var linkId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var cmd = KeycloakUserProvisioningCommandFactory.CreateInsertIdentityLinkWithIdCommand(
            connection: null!, transaction: null,
            identityLinkId: linkId, userId: userId,
            subject: "kc-sub-1", email: "user@example.com");

        cmd.Parameters.Should().HaveCount(4);
        cmd.Parameters["id"].Value.Should().Be(linkId);
        cmd.Parameters["userId"].Value.Should().Be(userId);
        cmd.Parameters["subject"].Value.Should().Be("kc-sub-1");
        cmd.Parameters["email"].Value.Should().Be("user@example.com");
    }
}

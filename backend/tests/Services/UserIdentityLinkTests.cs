using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class UserIdentityLinkQueryContractTests
{
    [Fact]
    public void BuildSelectActiveUserByExternalIdentitySql_ProjectsUserIdEmailDisplayName()
    {
        var sql = UserIdentityLinkQueryContract.BuildSelectActiveUserByExternalIdentitySql();

        sql.Should().Contain("SELECT u.id, u.email, u.display_name");
        sql.Should().Contain("FROM users u");
        sql.Should().Contain("INNER JOIN user_identities ui ON u.id = ui.user_id");
    }

    [Fact]
    public void BuildSelectActiveUserByExternalIdentitySql_FiltersByKeycloakProviderSubjectAndActiveUser()
    {
        var sql = UserIdentityLinkQueryContract.BuildSelectActiveUserByExternalIdentitySql();

        sql.Should().Contain("ui.provider = 'keycloak'");
        sql.Should().Contain("ui.provider_subject = @subject");
        sql.Should().Contain("u.status = 'active'");
    }

    [Fact]
    public void BuildInsertIdentityLinkSql_InsertsKeycloakLinkAndIsIdempotentByProviderSubject()
    {
        var sql = UserIdentityLinkQueryContract.BuildInsertIdentityLinkSql();

        sql.Should().Contain("INSERT INTO user_identities (user_id, provider, provider_subject, provider_email, created_at)");
        sql.Should().Contain("VALUES (@userId, 'keycloak', @subject, @email, NOW())");
        // Drift guard: idempotency depends on the unique key on (provider, provider_subject).
        sql.Should().Contain("ON CONFLICT (provider, provider_subject) DO NOTHING");
    }

    [Fact]
    public void BuildUpdateLastLoginSql_UpdatesLastLoginAndUpdatedAtById()
    {
        var sql = UserIdentityLinkQueryContract.BuildUpdateLastLoginSql();

        sql.Should().Contain("UPDATE users");
        sql.Should().Contain("SET last_login_at = NOW(), updated_at = NOW()");
        sql.Should().Contain("WHERE id = @id");
    }

    [Fact]
    public void Constants_AreStable()
    {
        UserIdentityLinkQueryContract.SubjectParameterName.Should().Be("subject");
        UserIdentityLinkQueryContract.UserIdParameterName.Should().Be("userId");
        UserIdentityLinkQueryContract.EmailParameterName.Should().Be("email");
        UserIdentityLinkQueryContract.LastLoginUserIdParameterName.Should().Be("id");
        UserIdentityLinkQueryContract.KeycloakProviderLiteral.Should().Be("keycloak");
    }
}

public class UserIdentityLinkCommandFactoryTests
{
    [Fact]
    public void CreateSelectActiveUserByExternalIdentityCommand_BindsSubjectParameter()
    {
        using var cmd = UserIdentityLinkCommandFactory.CreateSelectActiveUserByExternalIdentityCommand(
            connection: null!, externalSubject: "kc-sub-1");

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("subject");
        cmd.Parameters[0].Value.Should().Be("kc-sub-1");
    }

    [Fact]
    public void CreateInsertIdentityLinkCommand_BindsUserIdSubjectAndEmailParameters()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserIdentityLinkCommandFactory.CreateInsertIdentityLinkCommand(
            connection: null!, userId: userId, subject: "kc-sub-1", email: "user@example.com");

        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters["userId"].Value.Should().Be(userId);
        cmd.Parameters["subject"].Value.Should().Be("kc-sub-1");
        cmd.Parameters["email"].Value.Should().Be("user@example.com");
    }

    [Fact]
    public void CreateInsertIdentityLinkCommand_BindsDbNullForMissingEmail()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserIdentityLinkCommandFactory.CreateInsertIdentityLinkCommand(
            connection: null!, userId: userId, subject: "kc-sub-1", email: null);

        cmd.Parameters["email"].Value.Should().Be(DBNull.Value);
    }

    [Fact]
    public void CreateUpdateLastLoginCommand_BindsIdParameter()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserIdentityLinkCommandFactory.CreateUpdateLastLoginCommand(connection: null!, userId);

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("id");
        cmd.Parameters[0].Value.Should().Be(userId);
    }

    [Fact]
    public void CreateSelectIdentitiesByUserIdCommand_BindsUserIdParameter()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserIdentityLinkCommandFactory.CreateSelectIdentitiesByUserIdCommand(
            connection: null!, userId);

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("userId");
        cmd.Parameters[0].Value.Should().Be(userId);
    }
}

public class UserIdentityListQueryContractTests
{
    [Fact]
    public void IdentitySelectColumns_AreStableAndInProjectionOrder()
    {
        UserIdentityLinkQueryContract.IdentitySelectColumns
            .Should().Be("id, provider, provider_subject, provider_email, created_at");
    }

    [Fact]
    public void BuildSelectIdentitiesByUserIdSql_SelectsCanonicalColumnsFilteredByUserId()
    {
        var sql = UserIdentityLinkQueryContract.BuildSelectIdentitiesByUserIdSql();

        sql.Should().Contain($"SELECT {UserIdentityLinkQueryContract.IdentitySelectColumns}");
        sql.Should().Contain("FROM user_identities");
        sql.Should().Contain("WHERE user_id = @userId");
    }
}

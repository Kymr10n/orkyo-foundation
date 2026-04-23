using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class UserLookupByIdQueryContractTests
{
    [Fact]
    public void BuildExistsByIdSql_UsesExistsForPlannerShortCircuit()
    {
        UserLookupByIdQueryContract.BuildExistsByIdSql()
            .Should().Be("SELECT EXISTS(SELECT 1 FROM users WHERE id = @userId)");
    }

    [Fact]
    public void UserIdParameterName_IsStable() =>
        UserLookupByIdQueryContract.UserIdParameterName.Should().Be("userId");
}

public class UserLookupByIdCommandFactoryTests
{
    [Fact]
    public void CreateExistsByIdCommand_BindsUserIdParameter()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserLookupByIdCommandFactory.CreateExistsByIdCommand(connection: null!, userId);

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("userId");
        cmd.Parameters[0].Value.Should().Be(userId);
    }
}

public class UserLookupByIdScalarFlowTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ReadExists_ReturnsBoolPassthrough(bool input, bool expected) =>
        UserLookupByIdScalarFlow.ReadExists(input).Should().Be(expected);

    [Fact]
    public void ReadExists_TreatsNullAsFalse() =>
        UserLookupByIdScalarFlow.ReadExists(null).Should().BeFalse();

    [Fact]
    public void ReadExists_TreatsNonBoolAsFalse() =>
        UserLookupByIdScalarFlow.ReadExists("true").Should().BeFalse();
}

public class UserIdentityLinkKeycloakLookupTests
{
    [Fact]
    public void BuildSelectKeycloakSubjectByUserIdSql_FiltersByKeycloakProviderAndLimitsToOne()
    {
        var sql = UserIdentityLinkQueryContract.BuildSelectKeycloakSubjectByUserIdSql();

        sql.Should().Contain("SELECT provider_subject FROM user_identities");
        sql.Should().Contain("WHERE user_id = @userId");
        sql.Should().Contain("provider = 'keycloak'");
        sql.Should().Contain("LIMIT 1");
    }

    [Fact]
    public void CreateSelectKeycloakSubjectByUserIdCommand_BindsUserIdParameter()
    {
        var userId = Guid.NewGuid();

        using var cmd = UserIdentityLinkCommandFactory.CreateSelectKeycloakSubjectByUserIdCommand(
            connection: null!, userId);

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("userId");
        cmd.Parameters[0].Value.Should().Be(userId);
    }

    [Fact]
    public void ReadKeycloakSubject_PassesThroughString() =>
        UserIdentityLinkScalarFlow.ReadKeycloakSubject("kc-sub-1").Should().Be("kc-sub-1");

    [Fact]
    public void ReadKeycloakSubject_TreatsNullAsNull() =>
        UserIdentityLinkScalarFlow.ReadKeycloakSubject(null).Should().BeNull();

    [Fact]
    public void ReadKeycloakSubject_TreatsNonStringAsNull() =>
        UserIdentityLinkScalarFlow.ReadKeycloakSubject(42).Should().BeNull();
}

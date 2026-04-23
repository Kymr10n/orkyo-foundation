using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class SiteSettingsQueryContractTests
{
    [Fact]
    public void BuildSelectAllSettingsSql_SelectsKeyAndValue()
    {
        var sql = SiteSettingsQueryContract.BuildSelectAllSettingsSql();

        sql.Should().Be("SELECT key, value FROM site_settings");
    }

    [Fact]
    public void BuildUpsertSettingSql_InsertsAndOnConflictUpdatesValueAndUpdatedAt()
    {
        var sql = SiteSettingsQueryContract.BuildUpsertSettingSql();

        sql.Should().Contain("INSERT INTO site_settings (key, value, category, updated_at)");
        sql.Should().Contain("VALUES (@key, @value, @category, NOW())");
        sql.Should().Contain("ON CONFLICT (key) DO UPDATE SET");
        sql.Should().Contain("value = @value");
        sql.Should().Contain("updated_at = NOW()");
        // Category should NOT be touched on conflict — settings keep their original category.
        sql.Should().NotContain("category = @category");
    }

    [Fact]
    public void BuildDeleteSettingSql_DeletesByKey()
    {
        var sql = SiteSettingsQueryContract.BuildDeleteSettingSql();

        sql.Should().Be("DELETE FROM site_settings WHERE key = @key");
    }

    [Fact]
    public void ParameterNames_AreStable()
    {
        SiteSettingsQueryContract.KeyParameterName.Should().Be("key");
        SiteSettingsQueryContract.ValueParameterName.Should().Be("value");
        SiteSettingsQueryContract.CategoryParameterName.Should().Be("category");
    }
}

public class SiteSettingsCommandFactoryTests
{
    [Fact]
    public void CreateSelectAllSettingsCommand_UsesContractSqlAndNoParameters()
    {
        using var cmd = SiteSettingsCommandFactory.CreateSelectAllSettingsCommand(connection: null!);

        cmd.CommandText.Should().Be(SiteSettingsQueryContract.BuildSelectAllSettingsSql());
        cmd.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void CreateUpsertSettingCommand_BindsKeyValueCategoryParameters()
    {
        using var cmd = SiteSettingsCommandFactory.CreateUpsertSettingCommand(
            connection: null!, key: "rate.limit", value: "100", category: "rate-limiting");

        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters["key"].Value.Should().Be("rate.limit");
        cmd.Parameters["value"].Value.Should().Be("100");
        cmd.Parameters["category"].Value.Should().Be("rate-limiting");
    }

    [Fact]
    public void CreateDeleteSettingCommand_BindsKeyParameter()
    {
        using var cmd = SiteSettingsCommandFactory.CreateDeleteSettingCommand(connection: null!, key: "rate.limit");

        cmd.Parameters.Should().ContainSingle();
        cmd.Parameters[0].ParameterName.Should().Be("key");
        cmd.Parameters[0].Value.Should().Be("rate.limit");
    }
}

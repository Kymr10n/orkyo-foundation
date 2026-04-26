using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class DiagnosticsQueryContractTests
{
    [Fact]
    public void BuildMigrationCountSql_TargetsMigrationTrackingTable() =>
        DiagnosticsQueryContract.BuildMigrationCountSql()
            .Should().Be("SELECT COUNT(*) FROM orkyo_schema_migrations");

    [Fact]
    public void RecentAuditActivityInterval_LocksTwoHourLookbackPolicy() =>
        DiagnosticsQueryContract.RecentAuditActivityInterval.Should().Be("2 hours");

    [Fact]
    public void BuildRecentAuditActivitySql_AggregatesMaxCreatedAtWithinFixedLookback()
    {
        var sql = DiagnosticsQueryContract.BuildRecentAuditActivitySql();

        sql.Should().Contain("SELECT MAX(created_at) FROM audit_events");
        sql.Should().Contain("WHERE created_at > NOW() - INTERVAL '2 hours'");
    }

    [Fact]
    public void BuildRecentAuditActivitySql_DoesNotParameterizeInterval()
    {
        // Postgres INTERVAL literals cannot be safely parameterized as a
        // single bind value across all client encodings; the policy value is
        // intentionally embedded as a SQL literal under contract control.
        DiagnosticsQueryContract.BuildRecentAuditActivitySql()
            .Should().NotContain("@");
    }
}

public class DiagnosticsCommandFactoryTests
{
    [Fact]
    public void CreateMigrationCountCommand_BindsNoParameters()
    {
        using var cmd = DiagnosticsCommandFactory.CreateMigrationCountCommand(connection: null!);

        cmd.Parameters.Should().BeEmpty();
        cmd.CommandText.Should().Be("SELECT COUNT(*) FROM orkyo_schema_migrations");
    }

    [Fact]
    public void CreateRecentAuditActivityCommand_BindsNoParameters()
    {
        using var cmd = DiagnosticsCommandFactory.CreateRecentAuditActivityCommand(connection: null!);

        cmd.Parameters.Should().BeEmpty();
        cmd.CommandText.Should().Contain("MAX(created_at)");
    }
}

public class DiagnosticsScalarFlowTests
{
    [Theory]
    [InlineData((long)0, 0)]
    [InlineData((long)42, 42)]
    public void ReadMigrationCount_ConvertsLongScalarToInt(long input, int expected) =>
        DiagnosticsScalarFlow.ReadMigrationCount(input).Should().Be(expected);

    [Fact]
    public void ReadMigrationCount_TreatsNullAsZero() =>
        DiagnosticsScalarFlow.ReadMigrationCount(null).Should().Be(0);

    [Fact]
    public void ReadMigrationCount_TreatsDBNullAsZero() =>
        DiagnosticsScalarFlow.ReadMigrationCount(DBNull.Value).Should().Be(0);

    [Fact]
    public void ReadRecentAuditActivity_PassesThroughDateTime()
    {
        var ts = new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc);

        DiagnosticsScalarFlow.ReadRecentAuditActivity(ts).Should().Be(ts);
    }

    [Fact]
    public void ReadRecentAuditActivity_TreatsNullAsNull() =>
        DiagnosticsScalarFlow.ReadRecentAuditActivity(null).Should().BeNull();

    [Fact]
    public void ReadRecentAuditActivity_TreatsDBNullAsNull() =>
        DiagnosticsScalarFlow.ReadRecentAuditActivity(DBNull.Value).Should().BeNull();

    [Fact]
    public void ReadRecentAuditActivity_TreatsNonDateTimeAsNull() =>
        DiagnosticsScalarFlow.ReadRecentAuditActivity("2026-04-23").Should().BeNull();
}

using Api.Services;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

public class AuditEventQueryContractTests
{
    [Fact]
    public void SelectColumns_LocksProjectionOrderAndIncludesMetadataAsText()
    {
        // Drift guard: AuditEventReaderFlow reads positionally.
        AuditEventQueryContract.SelectColumns.Should().Be(
            "id, actor_user_id, actor_type, action, target_type, target_id, " +
            "metadata::text, request_id, ip_address, created_at");
    }

    [Fact]
    public void BuildCountSql_WithoutWhereClause_OmitsTrailingSpace()
    {
        AuditEventQueryContract.BuildCountSql(string.Empty)
            .Should().Be("SELECT COUNT(*) FROM audit_events");
    }

    [Fact]
    public void BuildCountSql_WithWhereClause_AppendsIt()
    {
        AuditEventQueryContract.BuildCountSql("WHERE action = @action")
            .Should().Be("SELECT COUNT(*) FROM audit_events WHERE action = @action");
    }

    [Fact]
    public void BuildSelectPageSql_AlwaysOrdersByCreatedAtDescAndPagesByPageSizeOffset()
    {
        var sql = AuditEventQueryContract.BuildSelectPageSql(string.Empty);

        sql.Should().Contain("FROM audit_events");
        sql.Should().Contain("ORDER BY created_at DESC");
        sql.Should().Contain("LIMIT @pageSize OFFSET @offset");
    }

    [Fact]
    public void BuildSelectPageSql_InjectsCallerProvidedWhereClause()
    {
        var sql = AuditEventQueryContract.BuildSelectPageSql("WHERE action = @action");

        sql.Should().Contain("WHERE action = @action");
        sql.Should().Contain("ORDER BY created_at DESC");
    }
}

public class AuditEventFilterBinderTests
{
    [Fact]
    public void Build_AllFiltersNull_ProducesEmptyWhereClauseAndNoParameters()
    {
        var (whereClause, parameters) = AuditEventFilterBinder.Build(
            new AuditEventListFilter(null, null, null, null, null, null));

        whereClause.Should().BeEmpty();
        parameters.Should().BeEmpty();
    }

    [Fact]
    public void Build_AllFiltersPopulated_ProducesAndedClausesAndOrderedParameters()
    {
        var actorId = Guid.NewGuid();
        var from = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        var (whereClause, parameters) = AuditEventFilterBinder.Build(
            new AuditEventListFilter("user.created", actorId, "user", "abc", from, to));

        whereClause.Should().Be(
            "WHERE action = @action AND actor_user_id = @actorId AND target_type = @targetType " +
            "AND target_id = @targetId AND created_at >= @from AND created_at <= @to");
        parameters.Should().HaveCount(6);
        parameters[0].ParameterName.Should().Be("action");
        parameters[0].Value.Should().Be("user.created");
        parameters[1].Value.Should().Be(actorId);
        parameters[2].Value.Should().Be("user");
        parameters[3].Value.Should().Be("abc");
        parameters[4].Value.Should().Be(from);
        parameters[5].Value.Should().Be(to);
    }

    [Fact]
    public void Build_WhitespaceStringFilters_AreIgnored()
    {
        var (whereClause, parameters) = AuditEventFilterBinder.Build(
            new AuditEventListFilter("  ", null, "\t", "", null, null));

        whereClause.Should().BeEmpty();
        parameters.Should().BeEmpty();
    }
}

public class AuditEventCommandFactoryTests
{
    [Fact]
    public void CreateCountCommand_NoFilter_BindsNoParameters()
    {
        using var cmd = AuditEventCommandFactory.CreateCountCommand(
            connection: null!,
            new AuditEventListFilter(null, null, null, null, null, null));

        cmd.CommandText.Should().Be("SELECT COUNT(*) FROM audit_events");
        cmd.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void CreateSelectPageCommand_AlwaysAppendsPageSizeAndOffsetAfterFilterParameters()
    {
        var actorId = Guid.NewGuid();
        using var cmd = AuditEventCommandFactory.CreateSelectPageCommand(
            connection: null!,
            new AuditEventListFilter(null, actorId, null, null, null, null),
            pageSize: 25, offset: 50);

        cmd.Parameters.Should().HaveCount(3);
        cmd.Parameters[0].ParameterName.Should().Be("actorId");
        cmd.Parameters[0].Value.Should().Be(actorId);
        cmd.Parameters[1].ParameterName.Should().Be("pageSize");
        cmd.Parameters[1].Value.Should().Be(25);
        cmd.Parameters[2].ParameterName.Should().Be("offset");
        cmd.Parameters[2].Value.Should().Be(50);
    }
}

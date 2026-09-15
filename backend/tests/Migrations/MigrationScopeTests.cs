using Orkyo.Foundation.Migrations;
using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// The scope is how a shared-database edition (Community) learns which tenant-phase migrations
/// it must skip — replacing a filename substring match on "feedback" that any future migration
/// with that word in its name would have silently fallen into.
/// </summary>
public sealed class MigrationScopeTests
{
    [Fact]
    public void AScriptIsDefaultScoped_UnlessDeclared()
    {
        var script = new MigrationScript("V001__x", "m", MigrationTargetDatabase.Tenant, "SELECT 1;", "abc",
            Array.Empty<string>(), Array.Empty<string>());

        script.Scope.Should().Be(MigrationScope.Default);
    }

    [Theory]
    [InlineData("-- @scope: tenant-database-only")]
    [InlineData("--@scope:TENANT-DATABASE-ONLY")]
    [InlineData("   --  @scope:   tenant-database-only   ")]
    public void TheDirective_IsParsedFromTheFile(string line)
    {
        EmbeddedSqlLoader.ParseScope($"{line}\nSELECT 1;").Should().Be(MigrationScope.TenantDatabaseOnly);
    }

    [Theory]
    [InlineData("SELECT 1; -- @scope: tenant-database-only")]  // not a whole-line comment
    [InlineData("-- scope: tenant-database-only")]              // missing the @
    [InlineData("-- @scope: default")]
    [InlineData("SELECT 1;")]
    public void OtherLines_LeaveTheDefault(string line)
    {
        EmbeddedSqlLoader.ParseScope($"{line}\nSELECT 1;").Should().Be(MigrationScope.Default);
    }

    [Fact]
    public void AnUnknownValue_IsRefused_NotIgnored()
    {
        var act = () => EmbeddedSqlLoader.ParseScope("-- @scope: tenant-only\nSELECT 1;");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Unknown @scope value*");
    }

    [Fact]
    public void TheFoundationModule_MarksExactlyTheTwoLegacyFeedbackMigrations()
    {
        var scoped = new FoundationMigrationModule().GetMigrations()
            .Where(s => s.Scope == MigrationScope.TenantDatabaseOnly)
            .Select(s => s.Id)
            .ToList();

        scoped.Should().BeEquivalentTo(FoundationMigrationModule.TenantDatabaseOnlyIds);
        scoped.Should().BeEquivalentTo(["1240.foundation.feedback", "1630.foundation.drop_feedback"]);
    }

    [Fact]
    public void EveryMarkedId_ExistsInTheFoundationSet()
    {
        var ids = new FoundationMigrationModule().GetMigrations().Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        FoundationMigrationModule.TenantDatabaseOnlyIds.Should().BeSubsetOf(ids,
            "a renamed migration must surface here, not as a Community deployment that silently runs it");
    }
}

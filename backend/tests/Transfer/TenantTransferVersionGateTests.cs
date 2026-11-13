using Api.Repositories;
using Api.Services.Transfer;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Tests.Transfer;

public class TenantTransferVersionGateTests
{
    private sealed class Module(params MigrationScript[] scripts) : IMigrationModule
    {
        public string ModuleName => "test";
        public int Order => 1;
        public IReadOnlyCollection<MigrationScript> GetMigrations() => scripts;
    }

    private static MigrationScript Script(
        string id, string module = "foundation",
        MigrationTargetDatabase target = MigrationTargetDatabase.Tenant,
        MigrationScope scope = MigrationScope.Default) =>
        new(id, module, target, "SELECT 1", "checksum", [], scope);

    [Fact]
    public void Current_IsTheTenantPhaseFoundationSet_ThatBothEditionsRun()
    {
        var gate = new TenantTransferVersionGate(
        [
            new Module(
                Script("1200.foundation.b"),
                Script("1100.foundation.a"),
                Script("1010.foundation.users", target: MigrationTargetDatabase.ControlPlane),
                Script("1240.foundation.feedback", scope: MigrationScope.TenantDatabaseOnly)),
            new Module(Script("3000.community.bootstrap", module: "community")),
        ]);

        gate.Current.Should().Equal(["1100.foundation.a", "1200.foundation.b"],
            "a script only one edition runs cannot be part of a version the two must agree on");
    }

    [Fact]
    public void Current_WithoutMigrationModules_SaysWhatTheHostForgot()
    {
        var gate = new TenantTransferVersionGate([]);

        var act = () => gate.Current;

        act.Should().Throw<InvalidOperationException>().WithMessage("*AddFoundationMigrations*");
    }

    [Fact]
    public void CheckDatabase_AcceptsAJournalWithMoreThanTheGateKnows()
    {
        // A per-tenant database also holds the scripts only it runs.
        var gate = new TenantTransferVersionGate([new Module(Script("1100.foundation.a"))]);

        gate.CheckDatabase(["1100.foundation.a", "1240.foundation.feedback"]).Should().BeNull();
        gate.CheckDatabase([])!.Code.Should().Be(ApiErrorCodes.TransferSchemaBehind);
    }

    [Fact]
    public void UserMatcher_CountsAnArchiveUserOnce_AndPrefersTheIdMatch()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        var archive = new[]
        {
            new TransferArchiveUser(id, "old@example.test", "A", DateTimeOffset.UnixEpoch),
            new TransferArchiveUser(id, "old@example.test", "A", DateTimeOffset.UnixEpoch),
        };
        // The same person under the same id with a new email, and someone else who has
        // since taken the old email.
        var target = new[] { new TransferTargetUser(id, "new@example.test"), new TransferTargetUser(other, "OLD@example.test") };

        var match = TransferUserMatcher.Match(archive, target);

        match.IdMap.Should().Equal(new Dictionary<Guid, Guid> { [id] = id });
        match.Matched.Should().Be(1);
        match.Placeholders.Should().BeEmpty();
    }
}

using Orkyo.Migrations.Abstractions;
using Orkyo.Migrator;

namespace Orkyo.Foundation.Tests.Migrations;

/// <summary>
/// The orderer decides the sequence the runner applies across modules (foundation 1000,
/// saas 2000, community 3000) and refuses duplicate ids. Pure logic, so
/// it is pinned here rather than only through the products' end-to-end migrator tests.
/// </summary>
public sealed class MigrationOrdererTests
{
    private static MigrationScript Script(
        string id,
        string module = "m",
        MigrationTargetDatabase target = MigrationTargetDatabase.Tenant) =>
        new(id, module, target, "SELECT 1;", ChecksumPolicy.Compute(id), Array.Empty<string>());

    private sealed class Module(string name, int order, params MigrationScript[] scripts) : IMigrationModule
    {
        public string ModuleName => name;
        public int Order => order;
        public IReadOnlyCollection<MigrationScript> GetMigrations() => scripts;
    }

    [Fact]
    public void Order_SortsByModuleOrderThenByIdWithinAModule()
    {
        var foundation = new Module("foundation", 1000, Script("1900.foundation.b", "foundation"), Script("1100.foundation.a", "foundation"));
        var saas = new Module("saas", 2000, Script("2000.saas.a", "saas"));

        var ordered = MigrationOrderer.Order(new IMigrationModule[] { saas, foundation }, MigrationTargetDatabase.Tenant);

        ordered.Select(s => s.Id).Should().Equal("1100.foundation.a", "1900.foundation.b", "2000.saas.a");
    }

    [Fact]
    public void Order_BreaksEqualModuleOrderByModuleName()
    {
        var b = new Module("b-module", 1000, Script("1000.b", "b-module"));
        var a = new Module("a-module", 1000, Script("1000.a", "a-module"));

        var ordered = MigrationOrderer.Order(new IMigrationModule[] { b, a }, MigrationTargetDatabase.Tenant);

        ordered.Select(s => s.Module).Should().Equal("a-module", "b-module");
    }

    [Fact]
    public void Order_FiltersToTheRequestedTargetDatabase()
    {
        var module = new Module("m", 1000,
            Script("1000.cp", target: MigrationTargetDatabase.ControlPlane),
            Script("1010.tenant", target: MigrationTargetDatabase.Tenant));

        var controlPlane = MigrationOrderer.Order(new[] { module }, MigrationTargetDatabase.ControlPlane);
        var tenant = MigrationOrderer.Order(new[] { module }, MigrationTargetDatabase.Tenant);

        controlPlane.Select(s => s.Id).Should().Equal("1000.cp");
        tenant.Select(s => s.Id).Should().Equal("1010.tenant");
    }

    [Fact]
    public void Order_RejectsADuplicateIdAcrossModules()
    {
        var a = new Module("a", 1000, Script("1000.same", "a"));
        var b = new Module("b", 2000, Script("1000.same", "b"));

        var act = () => MigrationOrderer.Order(new IMigrationModule[] { a, b }, MigrationTargetDatabase.Tenant);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate migration id '1000.same'*");
    }

    [Fact]
    public void Order_RejectsNullModules()
    {
        var act = () => MigrationOrderer.Order(null!, MigrationTargetDatabase.Tenant);

        act.Should().Throw<ArgumentNullException>();
    }
}

using Api.Services.Transfer;
using Npgsql;
using Orkyo.Foundation.Tests.Integration;

namespace Orkyo.Foundation.Tests.Transfer;

/// <summary>
/// Holds <see cref="TenantTransferTables"/> to the schema the migrations actually build. The
/// catalog is hand-written; this is what makes it true. A migration that adds a table, a
/// foreign key, or a column holding a user id fails here until the catalog classifies it —
/// which is the moment someone has to decide whether the new data travels.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection(PostgresCollection.Name)]
public class TenantTransferTableConformanceTests(PostgresFixture fixture)
{
    private sealed record ForeignKey(string Table, string Column, string Parent);

    [Fact]
    public async Task Catalog_NamesExactlyTheLiveBaseTables()
    {
        var live = await QueryAsync(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
            r => r.GetString(0));

        TenantTransferTables.All.Select(t => t.Name).Should().BeEquivalentTo(live,
            "every tenant table is classified once, and the catalog names no table that does not exist");
    }

    [Fact]
    public async Task LoadOrder_PutsEveryTableAfterTheTablesItReferences()
    {
        var order = TenantTransferTables.All.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var catalog = TenantTransferTables.All.ToDictionary(t => t.Name);

        var outOfOrder = (await ForeignKeysAsync())
            .Where(fk => catalog[fk.Table].Class == TransferTableClass.Travels)
            .Where(fk => fk.Column != catalog[fk.Table].DeferredColumn && fk.Column != catalog[fk.Table].LateColumn)
            .Where(fk => order[fk.Parent] >= order[fk.Table])
            .Select(fk => $"{fk.Table}.{fk.Column} -> {fk.Parent}");

        outOfOrder.Should().BeEmpty("a table loads after every table it references; " +
            "a cycle is broken by naming one nullable column as the table's DeferredColumn or LateColumn");
    }

    [Fact]
    public async Task LateRows_LoadAfterTheTableTheyReference_AndBeforeTheTablesThatReferenceThem()
    {
        var order = TenantTransferTables.All.Select((t, i) => (t.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var foreignKeys = await ForeignKeysAsync();
        var late = TenantTransferTables.All.Where(t => t.LateColumn is not null).ToList();
        late.Should().NotBeEmpty();

        foreach (var table in late)
        {
            foreignKeys.Should().Contain(new ForeignKey(table.Name, table.LateColumn!, table.LateAfter!),
                $"{table.Name}.{table.LateColumn} is the reference that makes its rows late");
            order[table.LateAfter!].Should().BeGreaterThan(order[table.Name]);

            // A row that loads late is not there yet for a table placed in between. The
            // referenced table itself is the one exception: it points at the early rows only.
            foreignKeys.Where(fk => fk.Parent == table.Name && fk.Table != table.LateAfter)
                .Should().OnlyContain(fk => order[fk.Table] > order[table.LateAfter!],
                    $"a table that references {table.Name} loads after its late rows");
        }
    }

    [Fact]
    public async Task TravellingTables_ReferenceOnlyTablesAnImportFills()
    {
        var catalog = TenantTransferTables.All.ToDictionary(t => t.Name);

        var dangling = (await ForeignKeysAsync())
            .Where(fk => catalog[fk.Table].Class == TransferTableClass.Travels)
            .Where(fk => catalog[fk.Parent].Class is TransferTableClass.Kept or TransferTableClass.Erased)
            .Select(fk => $"{fk.Table}.{fk.Column} -> {fk.Parent}");

        dangling.Should().BeEmpty("a travelling row must not point at a row the archive does not carry");
    }

    [Fact]
    public async Task KeptTables_DoNotReferenceTablesAnImportErases()
    {
        var catalog = TenantTransferTables.All.ToDictionary(t => t.Name);

        var broken = (await ForeignKeysAsync())
            .Where(fk => catalog[fk.Table].Class == TransferTableClass.Kept)
            .Where(fk => catalog[fk.Parent].Class is TransferTableClass.Travels or TransferTableClass.Erased)
            .Select(fk => $"{fk.Table}.{fk.Column} -> {fk.Parent}");

        broken.Should().BeEmpty("the erase would fail on the foreign key, or cascade into a table that must survive");
    }

    [Fact]
    public async Task DeferredParentAndLateColumns_AreNullableReferencesOnTablesKeyedById()
    {
        var special = TenantTransferTables.All
            .SelectMany(t => new[] { (t.Name, Column: t.DeferredColumn), (t.Name, Column: t.ParentColumn), (t.Name, Column: t.LateColumn) })
            .Where(x => x.Column is not null)
            .ToList();
        special.Should().NotBeEmpty();

        foreach (var (table, column) in special)
        {
            var nullable = await ScalarAsync<string>(
                "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'public' AND table_name = @t AND column_name = @c",
                ("t", table), ("c", column!));
            nullable.Should().Be("YES", $"{table}.{column} is empty for the rows that load first");

            var key = await QueryAsync(@"
                SELECT a.attname || ':' || format_type(a.atttypid, a.atttypmod)
                FROM pg_constraint c
                JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = ANY(c.conkey)
                WHERE c.contype = 'p' AND c.conrelid = ('public.' || @t)::regclass",
                r => r.GetString(0), ("t", table));
            key.Should().Equal(["id:uuid"], $"{table} is addressed by id when the column is set afterwards");
        }
    }

    [Fact]
    public async Task UserColumns_CoverEveryTravellingColumnThatHoldsAUserId()
    {
        var catalog = TenantTransferTables.All.ToDictionary(t => t.Name);
        var travelling = TenantTransferTables.Travelling.Select(t => t.Name).ToArray();

        var byConstraint = (await ForeignKeysAsync())
            .Where(fk => fk.Parent == TenantTransferTables.Users && catalog[fk.Table].Class == TransferTableClass.Travels)
            .Select(fk => $"{fk.Table}.{fk.Column}");
        // Several tables hold a user id with no foreign key, on purpose, so the constraint
        // list alone would miss them. The name is the only other signal there is.
        var byName = await QueryAsync(@"
            SELECT table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = ANY(@tables) AND column_name LIKE '%user_id%'",
            r => r.GetString(0), ("tables", travelling));

        var declared = TenantTransferTables.Travelling.SelectMany(t => t.UserColumns.Select(c => $"{t.Name}.{c}"));
        declared.Should().BeEquivalentTo(byConstraint.Union(byName),
            "an import rewrites exactly the columns that hold a user id");
    }

    [Fact]
    public async Task Schema_HasNothingARowCopyCannotCarry()
    {
        (await ScalarAsync<long>("SELECT COUNT(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'S'"))
            .Should().Be(0, "a sequence would need its position carried across");
        (await ScalarAsync<long>("SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = 'public' AND is_generated = 'ALWAYS'"))
            .Should().Be(0, "a generated column cannot be inserted");

        var tenantIdColumns = await QueryAsync(
            "SELECT table_name FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'tenant_id'",
            r => r.GetString(0));
        tenantIdColumns.Should().Equal([TenantTransferTables.Assets],
            "an import rewrites the owning tenant's id in exactly this table");
    }

    private Task<List<ForeignKey>> ForeignKeysAsync() => QueryAsync(@"
        SELECT c.conrelid::regclass::text, a.attname, c.confrelid::regclass::text
        FROM pg_constraint c
        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = c.conkey[1]
        JOIN pg_namespace n ON n.oid = c.connamespace
        WHERE c.contype = 'f' AND n.nspname = 'public' AND c.conrelid <> c.confrelid",
        r => new ForeignKey(r.GetString(0), r.GetString(1), r.GetString(2)));

    private async Task<List<T>> QueryAsync<T>(
        string sql, Func<NpgsqlDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        await using var conn = await fixture.OpenTestTenantConnectionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(map(reader));
        return rows;
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        (await QueryAsync(sql, r => r.GetFieldValue<T>(0), parameters)).Single();
}

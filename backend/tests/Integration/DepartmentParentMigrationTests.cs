using System.Reflection;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// What migration 1900 does to the department parents it converts.
///
/// The smoke tests assert the shape 1900 leaves behind, but they run against a tenant with no
/// departments in it, so every branch of the conversion is untested there — a migration that did
/// nothing at all would pass them. This runs the migration's own SQL over rows built to hit each
/// branch: a name matching exactly one department, a name matching two, a name matching none, and
/// a pair pointing at each other.
///
/// The SQL is read from the embedded migration rather than restated here. A copy would prove the
/// copy works.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DepartmentParentMigrationTests
{
    private readonly PostgresFixture _fixture;

    public DepartmentParentMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    /// <summary>The <c>DO $$ … END $$;</c> body of 1900, without the surrounding transaction.</summary>
    private static string ConversionBlock()
    {
        var assembly = typeof(FoundationMigrationModule).Assembly;
        var name = assembly.GetManifestResourceNames().Single(
            n => n.EndsWith("1900.foundation.department_parent_row_ref.sql", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        var start = sql.IndexOf("DO $$", StringComparison.Ordinal);
        var end = sql.IndexOf("END $$;", StringComparison.Ordinal) + "END $$;".Length;
        return sql[start..end];
    }

    private static async Task ExecAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ScalarAsync<T>(NpgsqlConnection conn, NpgsqlTransaction tx, string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    [Fact]
    public async Task ConvertsWhatItCan_AndDropsWhatItCannot()
    {
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var instanceId = await ScalarAsync<Guid>(conn, tx,
            @"SELECT i.id FROM list_instances i
                JOIN list_definitions d ON d.id = i.list_definition_id
               WHERE d.name = 'Departments' AND d.scope = 'organization'");

        // Put the column back the way 1820 left it, so the migration has its own starting state
        // to act on. Everything here is rolled back.
        await ExecAsync(conn, tx,
            @"UPDATE list_columns c SET data_type = 'text'
                FROM list_definitions d
               WHERE d.id = c.list_definition_id
                 AND d.name = 'Departments' AND d.scope = 'organization' AND c.key = 'parent'");

        var ids = new Dictionary<string, Guid>();
        async Task AddRow(string label, string name, string? parent)
        {
            var id = Guid.NewGuid();
            ids[label] = id;
            await ExecAsync(conn, tx,
                @"INSERT INTO list_rows (id, list_instance_id, values)
                  VALUES (@id, @instanceId, jsonb_strip_nulls(
                      jsonb_build_object('name', @name::text, 'parent', @parent::text)))",
                ("id", id), ("instanceId", instanceId),
                ("name", name), ("parent", (object?)parent ?? DBNull.Value));
        }

        await AddRow("root", "Operations", null);
        await AddRow("child", "Operations North", "Operations");     // one match → converted
        await AddRow("twinA", "Quality", null);
        await AddRow("twinB", "Quality", null);                      // two rows share this name
        await AddRow("ambiguous", "Quality South", "Quality");       // two matches → dropped
        await AddRow("dangling", "Orphan", "Nowhere");               // no match → dropped
        await AddRow("loopA", "Loop A", "Loop B");                   // cycle → both dropped
        await AddRow("loopB", "Loop B", "Loop A");

        await ExecAsync(conn, tx, ConversionBlock());

        var dataType = await ScalarAsync<string>(conn, tx,
            @"SELECT c.data_type FROM list_columns c
                JOIN list_definitions d ON d.id = c.list_definition_id
               WHERE d.name = 'Departments' AND d.scope = 'organization' AND c.key = 'parent'");
        Assert.Equal("row_ref", dataType);

        async Task<string?> ParentOf(string label) => await ScalarAsync<string>(conn, tx,
            "SELECT values ->> 'parent' FROM list_rows WHERE id = @id", ("id", ids[label]));

        // The one unambiguous name becomes the id of the row it named.
        Assert.Equal(ids["root"].ToString(), await ParentOf("child"));

        // A name matching two rows, or none, loses the cell: kept as text it would fail row_ref
        // validation on every later save, and the row could be read and never edited again.
        Assert.Null(await ParentOf("ambiguous"));
        Assert.Null(await ParentOf("dangling"));

        // The text era allowed a loop the old foreign key forbade. Both rows on it are cleared,
        // because the service refuses to save a row whose parent chain returns to it.
        Assert.Null(await ParentOf("loopA"));
        Assert.Null(await ParentOf("loopB"));

        // A root never had a parent and does not acquire one.
        Assert.Null(await ParentOf("root"));

        await tx.RollbackAsync();
    }
}

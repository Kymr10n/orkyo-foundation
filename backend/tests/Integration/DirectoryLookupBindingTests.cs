using Api.Repositories;
using Api.Services;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// Which lookup fields a directory type gets on activation, when the tenant's lists are not where
/// the catalog expects them.
///
/// Activating a directory type binds it to the two organization lists migration 1820 created,
/// resolved by name. Those lists belong to the tenant: renaming one is theirs to do, and the
/// activation has to leave that field unbound rather than invent a replacement list or fail. Only
/// the happy path was covered, so nothing said what the skip does.
///
/// Runs against the isolated integration database: the setup renames a shared list, which no test
/// sharing a database with others could do safely. The name is restored in a finally.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DirectoryLookupBindingTests
{
    private readonly PostgresFixture _fixture;

    public DirectoryLookupBindingTests(PostgresFixture fixture) => _fixture = fixture;

    private ResourceTypeCatalogService BuildService()
    {
        var factory = _fixture.CreateConnectionFactory();
        var org = new OrgContext
        {
            OrgId = Guid.NewGuid(),
            OrgSlug = "test-tenant",
            DbConnectionString = _fixture.TestTenantConnectionString,
        };

        var types = new ResourceTypeRepository(org, factory);
        var definitions = new ListDefinitionRepository(org, factory);
        var instances = new ListInstanceRepository(org, factory);
        var fields = new ResourceCustomFieldRepository(org, factory);

        return new ResourceTypeCatalogService(
            types,
            new ResourceCustomFieldService(fields, types, definitions, instances),
            definitions,
            instances);
    }

    [Fact]
    public async Task ActivatingPerson_SkipsTheLookupWhoseListWasRenamed_AndBindsTheOther()
    {
        var service = BuildService();
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();

        async Task RenameAsync(string from, string to)
        {
            await using var cmd = new NpgsqlCommand(
                "UPDATE list_definitions SET name = @to WHERE name = @from AND scope = 'organization'",
                conn);
            cmd.Parameters.AddWithValue("from", from);
            cmd.Parameters.AddWithValue("to", to);
            await cmd.ExecuteNonQueryAsync();
        }

        var renamed = $"Trades {Guid.NewGuid():N}"[..16];
        await RenameAsync("Job Titles", renamed);

        try
        {
            var type = await service.ActivateAsync("person");
            Assert.NotNull(type);

            await using var read = new NpgsqlCommand(
                "SELECT key FROM resource_custom_fields WHERE resource_type_id = @id ORDER BY key",
                conn);
            read.Parameters.AddWithValue("id", type!.Id);

            var keys = new List<string>();
            await using (var reader = await read.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync()) keys.Add(reader.GetString(0));
            }

            // Departments still answers to its name, so that field is bound. Job Titles does not,
            // and the field is left off rather than bound to a list the tenant did not mean.
            Assert.Contains("department", keys);
            Assert.DoesNotContain("job_title", keys);
        }
        finally
        {
            await RenameAsync(renamed, "Job Titles");

            // The fields this activation created are the test's own doing; the isolated database
            // is shared across this collection, so they go with it.
            await using var cleanup = new NpgsqlCommand(
                @"DELETE FROM resource_custom_fields f
                    USING resource_types t
                   WHERE t.id = f.resource_type_id AND t.key = 'person'", conn);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}

using System.Security.Cryptography;
using Api.Models;
using Api.Repositories;
using Api.Security.Encryption;
using Api.Services;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// The difference between the two list reads, at the size where it starts to matter.
///
/// <c>GetAllAsync</c> stops at 1000 rows and reports neither the total nor the fact that it cut.
/// That is the right bound for a list view and the wrong one for anything that aggregates,
/// exports or schedules: a utilization figure computed over the first 1000 of 1001 resources is
/// not a partial answer, it is a wrong one. <c>GetEveryAsync</c> pages until the total is reached.
///
/// Runs against the isolated integration database with its own resource type, because the point
/// of the test is to hold more than a thousand rows for the duration.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ResourceCompleteReadTests
{
    private readonly PostgresFixture _fixture;

    public ResourceCompleteReadTests(PostgresFixture fixture) => _fixture = fixture;

    /// <summary>One more than the cap, so a read that stops at it is visibly short.</summary>
    private const int RowCount = 1001;

    [Fact]
    public async Task GetEveryAsync_ReadsPastTheCapThatGetAllAsyncStopsAt()
    {
        var typeKey = $"bulk_{Guid.NewGuid():N}"[..20];
        await using var conn = await _fixture.OpenTestTenantConnectionAsync();

        await using (var createType = new NpgsqlCommand(
            @"INSERT INTO resource_types
                  (key, display_name, display_name_plural, icon, is_system, is_active,
                   has_geometry, has_directory_profile, single_group_membership)
              VALUES (@key, 'Bulk', 'Bulk', 'Box', false, true, false, false, false)", conn))
        {
            createType.Parameters.AddWithValue("key", typeKey);
            await createType.ExecuteNonQueryAsync();
        }

        try
        {
            // One statement rather than a thousand round trips — the suite is not the place to
            // spend twenty seconds proving arithmetic.
            await using (var fill = new NpgsqlCommand(
                @"INSERT INTO resources (resource_type_id, name, allocation_mode, is_active)
                  SELECT t.id, 'Bulk ' || lpad(g::text, 5, '0'), 'Exclusive', true
                    FROM resource_types t, generate_series(1, @n) g
                   WHERE t.key = @key", conn))
            {
                fill.Parameters.AddWithValue("key", typeKey);
                fill.Parameters.AddWithValue("n", RowCount);
                await fill.ExecuteNonQueryAsync();
            }

            var repo = new ResourceRepository(
                new OrgContext
                {
                    OrgId = Guid.NewGuid(),
                    OrgSlug = "test-tenant",
                    DbConnectionString = _fixture.TestTenantConnectionString,
                },
                _fixture.CreateConnectionFactory(),
                new AesGcmEncryptionService(RandomNumberGenerator.GetBytes(32)));

            var filter = new ResourceListFilter { IsActive = true, ResourceTypeKey = typeKey };

            var capped = await repo.GetAllAsync(filter);
            var every = await repo.GetEveryAsync(filter);

            Assert.Equal(1000, capped.Count);
            Assert.Equal(RowCount, every.Count);

            // Paging must not double-count or skip across the page boundary, which a count alone
            // would not catch — an off-by-one offset returns the right number of wrong rows.
            Assert.Equal(RowCount, every.Select(r => r.Id).Distinct().Count());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand(
                @"DELETE FROM resources r USING resource_types t
                   WHERE t.id = r.resource_type_id AND t.key = @key;
                  DELETE FROM resource_types WHERE key = @key", conn);
            cleanup.Parameters.AddWithValue("key", typeKey);
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}

using Api.Repositories;
using Api.Services;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for the foundation-owned <see cref="SiteSettingsRepository"/>
/// using an in-foundation test connection factory. Exercises the
/// <c>site_settings</c> table end-to-end (upsert semantics, category-on-conflict
/// immutability, delete).
///
/// Each test uses a unique per-test key so parallel / sequential test runs
/// don't interfere. The control-plane DB is shared across the test class; rows
/// are cleaned up at the end of each test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SiteSettingsRepositoryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public SiteSettingsRepositoryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UpsertAsync_ShouldInsertNewRow_WhenKeyMissing()
    {
        var repo = BuildRepo();
        var key = UniqueKey();

        try
        {
            await repo.UpsertAsync(key, "42", "security");

            var all = await repo.GetAllAsync();
            all.Should().ContainKey(key).WhoseValue.Should().Be("42");
        }
        finally
        {
            await repo.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task UpsertAsync_ShouldUpdateValue_WhenKeyExists()
    {
        var repo = BuildRepo();
        var key = UniqueKey();

        try
        {
            await repo.UpsertAsync(key, "first", "branding");
            await repo.UpsertAsync(key, "second", "branding");

            var all = await repo.GetAllAsync();
            all.Should().ContainKey(key).WhoseValue.Should().Be("second");
        }
        finally
        {
            await repo.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task UpsertAsync_ShouldLeaveCategoryUnchanged_OnConflict()
    {
        var repo = BuildRepo();
        var key = UniqueKey();

        try
        {
            await repo.UpsertAsync(key, "initial", "security");
            // Second upsert attempts to change the category; the contract says category
            // is immutable on conflict (only value updates).
            await repo.UpsertAsync(key, "updated", "branding");

            var category = await ReadCategoryAsync(key);
            category.Should().Be("security", "category is intentionally immutable on upsert conflict");
        }
        finally
        {
            await repo.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveRow_AndReturnTrue()
    {
        var repo = BuildRepo();
        var key = UniqueKey();
        await repo.UpsertAsync(key, "to-delete", "general");

        var deleted = await repo.DeleteAsync(key);

        deleted.Should().BeTrue();
        var all = await repo.GetAllAsync();
        all.Should().NotContainKey(key);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalse_WhenKeyMissing()
    {
        var repo = BuildRepo();

        var deleted = await repo.DeleteAsync(UniqueKey());

        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllAsync_ShouldReturn_CaseInsensitiveDictionary()
    {
        var repo = BuildRepo();
        var key = UniqueKey();
        await repo.UpsertAsync(key, "mixed-case-probe", "general");

        try
        {
            var all = await repo.GetAllAsync();

            all.Should().ContainKey(key.ToUpperInvariant());
            all.Should().ContainKey(key.ToLowerInvariant());
        }
        finally
        {
            await repo.DeleteAsync(key);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private SiteSettingsRepository BuildRepo()
    {
        var factory = _fixture.CreateConnectionFactory();
        return new SiteSettingsRepository(factory);
    }


    private static string UniqueKey() => $"test.{Guid.NewGuid():N}";

    private async Task<string?> ReadCategoryAsync(string key)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT category FROM site_settings WHERE key = @k", conn);
        cmd.Parameters.AddWithValue("k", key);
        return (string?)await cmd.ExecuteScalarAsync();
    }
}

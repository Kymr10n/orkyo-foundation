using Api.Models;
using Api.Repositories;
using Npgsql;

namespace Orkyo.Foundation.Tests.Helpers;

// Tests now target the NpgsqlQueryExtensions methods that absorbed DbQueryHelper.

public class NpgsqlQueryExtensionsAllowlistTests
{
    [Theory]
    [InlineData("sites")]
    [InlineData("spaces")]
    [InlineData("resource_groups")]
    [InlineData("criteria")]
    [InlineData("requests")]
    [InlineData("users")]
    [InlineData("tenants")]
    [InlineData("announcements")]
    [InlineData("presets")]
    [InlineData("templates")]
    public async Task ExistsAsync_AllowedTable_DoesNotThrow(string table)
    {
        // The allowlist check passes; a null connection causes an infrastructure exception
        // (not an ArgumentException about the table name).
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => ((NpgsqlConnection)null!).ExistsAsync(table, Guid.NewGuid()));

        Assert.IsNotType<ArgumentException>(ex);
    }

    [Theory]
    [InlineData("drop_table")]
    [InlineData("admin_secrets")]
    [InlineData("users; DROP TABLE sites;--")]
    [InlineData("")]
    [InlineData("SITES")] // case-sensitive
    public async Task ExistsAsync_DisallowedTable_ThrowsArgumentException(string table)
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => ((NpgsqlConnection)null!).ExistsAsync(table, Guid.NewGuid()));

        Assert.Contains("not in the allowed table list", ex.Message);
    }
}

[Collection("Database collection")]
public class NpgsqlQueryExtensionsIntegrationTests
{
    private readonly string _tenantCs;

    public NpgsqlQueryExtensionsIntegrationTests(DatabaseFixture fixture)
    {
        _tenantCs =
            $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        return conn;
    }

    // --- ExistsAsync ---

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenRecordExists()
    {
        await using var conn = await OpenAsync();
        await using var getCmd = new NpgsqlCommand("SELECT id FROM criteria LIMIT 1", conn);
        var id = (Guid)(await getCmd.ExecuteScalarAsync())!;

        var result = await conn.ExistsAsync("criteria", id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenRecordNotFound()
    {
        await using var conn = await OpenAsync();

        var result = await conn.ExistsAsync("criteria", Guid.NewGuid());

        result.Should().BeFalse();
    }

    // --- ExecuteAsync (replaces ExecuteDeleteAsync) ---

    [Fact]
    public async Task ExecuteAsync_ReturnsOneAffectedRow_AfterInsert()
    {
        await using var conn = await OpenAsync();
        var id = Guid.NewGuid();

        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO criteria (id, name, description, data_type, created_at, updated_at) VALUES (@id, @name, '', 'Boolean', NOW(), NOW())",
            conn);
        insertCmd.Parameters.AddWithValue("id", id);
        insertCmd.Parameters.AddWithValue("name", $"_del_test_{id:N}");
        await insertCmd.ExecuteNonQueryAsync();

        var rows = await conn.ExecuteAsync("DELETE FROM criteria WHERE id = @id",
            p => p.AddWithValue("id", id));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZero_WhenRecordNotFound()
    {
        await using var conn = await OpenAsync();

        var rows = await conn.ExecuteAsync("DELETE FROM criteria WHERE id = @id",
            p => p.AddWithValue("id", Guid.NewGuid()));

        rows.Should().Be(0);
    }

    // --- QueryPagedAsync ---

    [Fact]
    public async Task QueryPagedAsync_ReturnsPaginatedSubset()
    {
        await using var conn = await OpenAsync();

        var result = await conn.QueryPagedAsync(
            new PageRequest { Page = 1, PageSize = 2 },
            "SELECT COUNT(*) FROM criteria",
            "SELECT name FROM criteria ORDER BY name LIMIT @limit OFFSET @offset",
            bind: null,
            map: r => r.GetString(0));

        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().BeGreaterThanOrEqualTo(4);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task QueryPagedAsync_UsesBindCountParams_WhenProvided()
    {
        await using var conn = await OpenAsync();

        var result = await conn.QueryPagedAsync(
            new PageRequest { Page = 1, PageSize = 10 },
            "SELECT COUNT(*) FROM criteria WHERE data_type = @dt",
            "SELECT name FROM criteria WHERE data_type = @dt ORDER BY name LIMIT @limit OFFSET @offset",
            bind: p => p.AddWithValue("dt", "Boolean"),
            map: r => r.GetString(0),
            bindCount: p => p.AddWithValue("dt", "Boolean"));

        result.Items.Should().NotBeEmpty();
    }
}

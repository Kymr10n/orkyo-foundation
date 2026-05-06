using Api.Helpers;
using Api.Models;
using Npgsql;

namespace Orkyo.Foundation.Tests.Helpers;

public class DbQueryHelperTests
{
    [Theory]
    [InlineData("sites")]
    [InlineData("spaces")]
    [InlineData("space_groups")]
    [InlineData("criteria")]
    [InlineData("requests")]
    [InlineData("users")]
    [InlineData("tenants")]
    [InlineData("announcements")]
    [InlineData("presets")]
    [InlineData("templates")]
    public async Task ExistsAsync_AllowedTable_DoesNotThrow(string table)
    {
        // Verify the allowlist check passes (NpgsqlConnection will be null,
        // causing an InvalidOperationException after the check passes).
        var ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => DbQueryHelper.ExistsAsync(null!, table, Guid.NewGuid()));

        // Error must be about the connection, NOT the allowlist
        Assert.Contains("Connection", ex.Message);
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
            () => DbQueryHelper.ExistsAsync(null!, table, Guid.NewGuid()));

        Assert.Contains("not in the allowed table list", ex.Message);
    }
}

[Collection("Database collection")]
public class DbQueryHelperIntegrationTests
{
    private readonly string _tenantCs;

    public DbQueryHelperIntegrationTests(DatabaseFixture fixture)
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

    // --- ExistsAsync (real connection) ---

    [Fact]
    public async Task ExistsAsync_ReturnsTrue_WhenRecordExists()
    {
        await using var conn = await OpenAsync();
        await using var getCmd = new NpgsqlCommand("SELECT id FROM criteria LIMIT 1", conn);
        var id = (Guid)(await getCmd.ExecuteScalarAsync())!;

        var result = await DbQueryHelper.ExistsAsync(conn, "criteria", id);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalse_WhenRecordNotFound()
    {
        await using var conn = await OpenAsync();

        var result = await DbQueryHelper.ExistsAsync(conn, "criteria", Guid.NewGuid());

        result.Should().BeFalse();
    }

    // --- ExecuteDeleteAsync ---

    [Fact]
    public async Task ExecuteDeleteAsync_ReturnsOneAffectedRow_AfterInsert()
    {
        await using var conn = await OpenAsync();
        var id = Guid.NewGuid();

        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO criteria (id, name, description, data_type, created_at, updated_at) VALUES (@id, @name, '', 'Boolean', NOW(), NOW())",
            conn);
        insertCmd.Parameters.AddWithValue("id", id);
        insertCmd.Parameters.AddWithValue("name", $"_del_test_{id:N}");
        await insertCmd.ExecuteNonQueryAsync();

        var rows = await DbQueryHelper.ExecuteDeleteAsync(conn, "DELETE FROM criteria WHERE id = @id", id);

        rows.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteDeleteAsync_ReturnsZero_WhenRecordNotFound()
    {
        await using var conn = await OpenAsync();

        var rows = await DbQueryHelper.ExecuteDeleteAsync(conn, "DELETE FROM criteria WHERE id = @id", Guid.NewGuid());

        rows.Should().Be(0);
    }

    // --- ExecutePagedQueryAsync ---

    [Fact]
    public async Task ExecutePagedQueryAsync_ReturnsPaginatedSubset()
    {
        await using var conn = await OpenAsync();

        var result = await DbQueryHelper.ExecutePagedQueryAsync<string>(
            conn,
            new PageRequest { Page = 1, PageSize = 2 },
            "SELECT COUNT(*) FROM criteria",
            "SELECT name FROM criteria ORDER BY name LIMIT @limit OFFSET @offset",
            null,
            r => r.GetString(0));

        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().BeGreaterThanOrEqualTo(4);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task ExecutePagedQueryAsync_UsesAddCountParams_WhenProvided()
    {
        await using var conn = await OpenAsync();

        var result = await DbQueryHelper.ExecutePagedQueryAsync<string>(
            conn,
            new PageRequest { Page = 1, PageSize = 10 },
            "SELECT COUNT(*) FROM criteria WHERE data_type = @dt",
            "SELECT name FROM criteria WHERE data_type = @dt ORDER BY name LIMIT @limit OFFSET @offset",
            cmd => cmd.Parameters.AddWithValue("dt", "Boolean"),
            r => r.GetString(0),
            cmd => cmd.Parameters.AddWithValue("dt", "Boolean"));

        result.Items.Should().NotBeEmpty();
    }
}

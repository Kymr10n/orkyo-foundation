using Api.Helpers;
using Npgsql;

namespace Orkyo.Foundation.Tests.Helpers;

/// <summary>
/// Integration tests for <see cref="ReaderExtensions"/>.
/// Each test issues a literal SELECT that returns a known value, then exercises
/// the corresponding extension method through a real <see cref="NpgsqlDataReader"/>.
/// </summary>
[Collection("Database collection")]
public class ReaderExtensionsTests
{
    private readonly string _connectionString;

    public ReaderExtensionsTests(DatabaseFixture fixture)
    {
        _connectionString =
            $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    // All types in one literal row so that each individual test can open its own
    // reader without repeating the connection setup.
    private const string LiteralQuery = @"
        SELECT
            '11111111-2222-3333-4444-555555555555'::uuid      AS guid_col,
            NULL::uuid                                        AS nullable_guid_null,
            '11111111-2222-3333-4444-555555555555'::uuid      AS nullable_guid_set,
            'hello world'::text                               AS string_col,
            NULL::text                                        AS nullable_string_null,
            'not null string'::text                           AS nullable_string_set,
            42::integer                                       AS int_col,
            NULL::integer                                     AS nullable_int_null,
            99::integer                                       AS nullable_int_set,
            true::boolean                                     AS bool_col,
            '2024-06-15 12:00:00'::timestamp                  AS dt_col,
            NULL::timestamp                                   AS nullable_dt_null,
            '2024-06-15 12:00:00'::timestamp                  AS nullable_dt_set";

    private async Task<NpgsqlDataReader> OpenReaderAsync()
    {
        var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        var cmd = new NpgsqlCommand(LiteralQuery, conn);
        // CloseConnection disposes the connection when the reader is disposed
        return await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.CloseConnection);
    }

    // --- GetString ---

    [Fact]
    public async Task GetString_ReturnsValueByColumnName()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetString("string_col").Should().Be("hello world");
    }

    // --- GetNullableString ---

    [Fact]
    public async Task GetNullableString_ReturnsNull_WhenColumnIsNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableString("nullable_string_null").Should().BeNull();
    }

    [Fact]
    public async Task GetNullableString_ReturnsValue_WhenColumnIsNotNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableString("nullable_string_set").Should().Be("not null string");
    }

    // --- GetGuid ---

    [Fact]
    public async Task GetGuid_ReturnsValueByColumnName()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetGuid("guid_col").Should().Be(new Guid("11111111-2222-3333-4444-555555555555"));
    }

    // --- GetNullableGuid ---

    [Fact]
    public async Task GetNullableGuid_ReturnsNull_WhenColumnIsNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableGuid("nullable_guid_null").Should().BeNull();
    }

    [Fact]
    public async Task GetNullableGuid_ReturnsValue_WhenColumnIsNotNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableGuid("nullable_guid_set").Should().Be(new Guid("11111111-2222-3333-4444-555555555555"));
    }

    // --- GetInt32 ---

    [Fact]
    public async Task GetInt32_ReturnsValueByColumnName()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetInt32("int_col").Should().Be(42);
    }

    // --- GetNullableInt32 ---

    [Fact]
    public async Task GetNullableInt32_ReturnsNull_WhenColumnIsNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableInt32("nullable_int_null").Should().BeNull();
    }

    [Fact]
    public async Task GetNullableInt32_ReturnsValue_WhenColumnIsNotNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableInt32("nullable_int_set").Should().Be(99);
    }

    // --- GetBoolean ---

    [Fact]
    public async Task GetBoolean_ReturnsValueByColumnName()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetBoolean("bool_col").Should().BeTrue();
    }

    // --- GetDateTime ---

    [Fact]
    public async Task GetDateTime_ReturnsValueByColumnName()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetDateTime("dt_col").Should().Be(new DateTime(2024, 6, 15, 12, 0, 0));
    }

    // --- GetNullableDateTime ---

    [Fact]
    public async Task GetNullableDateTime_ReturnsNull_WhenColumnIsNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableDateTime("nullable_dt_null").Should().BeNull();
    }

    [Fact]
    public async Task GetNullableDateTime_ReturnsValue_WhenColumnIsNotNull()
    {
        await using var reader = await OpenReaderAsync();
        await reader.ReadAsync();
        reader.GetNullableDateTime("nullable_dt_set").Should().Be(new DateTime(2024, 6, 15, 12, 0, 0));
    }
}

using Api.Helpers;
using Api.Services;
using Npgsql;
using System.Text.Json;

namespace Api.Repositories;

public class UserPreferencesRepository : IUserPreferencesRepository
{
    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public UserPreferencesRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    public async Task<JsonDocument?> GetPreferencesAsync(Guid userId)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT preferences FROM user_preferences WHERE user_id = @userId",
            conn
        );
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var json = reader.GetString(0);
            return JsonDocument.Parse(json);
        }

        return null;
    }

    public async Task<bool> UpdatePreferencesAsync(Guid userId, JsonDocument preferences)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO user_preferences (user_id, preferences, updated_at)
              VALUES (@userId, @preferences::jsonb, NOW())
              ON CONFLICT (user_id) 
              DO UPDATE SET preferences = @preferences::jsonb, updated_at = NOW()",
            conn
        );
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("preferences", preferences.RootElement.GetRawText());

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}

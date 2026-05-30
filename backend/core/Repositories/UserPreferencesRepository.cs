using System.Text.Json;
using Api.Helpers;
using Api.Services;

namespace Api.Repositories;

public class UserPreferencesRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IUserPreferencesRepository
{
    public async Task<JsonDocument?> GetPreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        var json = await conn.QuerySingleOrDefaultAsync(
            "SELECT preferences FROM user_preferences WHERE user_id = @userId",
            p => p.AddWithValue("userId", userId),
            r => r.GetString(0), ct);
        return json is null ? null : JsonDocument.Parse(json);
    }

    public async Task<bool> UpdatePreferencesAsync(Guid userId, JsonDocument preferences, CancellationToken ct = default)
    {
        await using var conn = connectionFactory.CreateOrgConnection(orgContext);
        return await conn.ExecuteAsync(@"
            INSERT INTO user_preferences (user_id, preferences, updated_at)
            VALUES (@userId, @preferences::jsonb, NOW())
            ON CONFLICT (user_id)
            DO UPDATE SET preferences = @preferences::jsonb, updated_at = NOW()",
            p =>
            {
                p.AddWithValue("userId", userId);
                p.AddWithValue("preferences", preferences.RootElement.GetRawText());
            }, ct) > 0;
    }
}

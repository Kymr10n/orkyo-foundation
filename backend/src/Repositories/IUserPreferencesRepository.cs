using System.Text.Json;

namespace Api.Repositories;

public interface IUserPreferencesRepository
{
    Task<JsonDocument?> GetPreferencesAsync(Guid userId);
    Task<bool> UpdatePreferencesAsync(Guid userId, JsonDocument preferences);
}

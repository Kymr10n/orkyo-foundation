using System.Text.Json;

namespace Api.Repositories;

/// <summary>Persistence layer for per-user UI preference blobs stored as JSON.</summary>
public interface IUserPreferencesRepository
{
    /// <summary>
    /// Returns the user's preferences JSON document, or <c>null</c> if none have been saved yet.
    /// </summary>
    Task<JsonDocument?> GetPreferencesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Upserts the preferences document for the given user. Returns <c>false</c> only on
    /// an unexpected persistence failure (non-throwing path; callers should log).
    /// </summary>
    Task<bool> UpdatePreferencesAsync(Guid userId, JsonDocument preferences, CancellationToken ct = default);
}

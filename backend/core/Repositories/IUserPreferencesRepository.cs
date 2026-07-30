using System.Text.Json;

namespace Api.Repositories;

/// <summary>Persistence layer for per-user UI preference blobs stored as JSON.</summary>
public interface IUserPreferencesRepository
{
    /// <summary>
    /// Returns the user's preferences JSON document, or <c>null</c> if none have been saved yet.
    /// </summary>
    /// <remarks>
    /// <b>The caller owns the returned document and must dispose it</b> — <c>JsonDocument</c>
    /// holds a buffer rented from <c>ArrayPool</c>. If the value is handed to an <c>IResult</c>,
    /// clone the element out first (<c>doc.RootElement.Clone()</c>): results serialize after the
    /// handler returns, so returning the document itself under a <c>using</c> disposes it before
    /// the response body is written.
    /// </remarks>
    Task<JsonDocument?> GetPreferencesAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Upserts the preferences document for the given user. Returns <c>false</c> only on
    /// an unexpected persistence failure (non-throwing path; callers should log).
    /// </summary>
    Task<bool> UpdatePreferencesAsync(Guid userId, JsonDocument preferences, CancellationToken ct = default);
}

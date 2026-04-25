using System.Collections.Concurrent;

namespace Api.Services.BffSession;

/// <summary>
/// PKCE state stored during the OAuth2 authorization code flow.
/// </summary>
public sealed record PkceStateData(string CodeVerifier, string ReturnTo);

/// <summary>
/// Storage abstraction for short-lived PKCE state entries.
/// Implementations MUST make <see cref="GetAndRemoveAsync"/> atomic to prevent
/// TOCTOU replay attacks (two concurrent callbacks with the same state value).
/// </summary>
public interface IBffPkceStateStore
{
    /// <summary>Stores a state entry with the given TTL.</summary>
    Task SetAsync(string state, PkceStateData data, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// Atomically retrieves and removes the state entry.
    /// Returns null if the state is not found or has expired.
    /// One-time use: a second call with the same state always returns null.
    /// </summary>
    Task<PkceStateData?> GetAndRemoveAsync(string state, CancellationToken ct = default);
}

/// <summary>
/// In-memory PKCE state store for single-instance deployments (development / test).
/// Uses <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove"/> for atomic get-and-delete.
/// </summary>
public sealed class InMemoryBffPkceStateStore : IBffPkceStateStore
{
    private sealed record Entry(PkceStateData Data, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _store = new();

    public Task SetAsync(string state, PkceStateData data, TimeSpan ttl, CancellationToken ct = default)
    {
        _store[state] = new Entry(data, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    public Task<PkceStateData?> GetAndRemoveAsync(string state, CancellationToken ct = default)
    {
        // TryRemove is atomic: only the first caller succeeds, making replay impossible.
        if (!_store.TryRemove(state, out var entry))
            return Task.FromResult<PkceStateData?>(null);

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            return Task.FromResult<PkceStateData?>(null);

        return Task.FromResult<PkceStateData?>(entry.Data);
    }
}

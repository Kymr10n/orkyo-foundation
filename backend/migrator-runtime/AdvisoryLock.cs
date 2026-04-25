using Npgsql;

namespace Orkyo.Migrator;

/// <summary>
/// Postgres advisory lock helper. Prevents concurrent migration runs against the same
/// database (e.g. two pods rolling out simultaneously). The lock is keyed by a stable
/// hash of a caller-supplied key string so SaaS can use distinct keys for control-plane
/// vs each tenant DB.
/// </summary>
internal static class AdvisoryLock
{
    public static async Task<IAsyncDisposable> AcquireAsync(
        NpgsqlConnection connection,
        string key,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var lockId = StableHash64(key);
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            cmd.Parameters.AddWithValue("key", lockId);
            var acquired = (bool)(await cmd.ExecuteScalarAsync(ct) ?? false);
            if (acquired) return new Lease(connection, lockId);

            if (DateTime.UtcNow >= deadline)
            {
                throw new InvalidOperationException(
                    $"Could not acquire migration advisory lock for key '{key}' within {timeout.TotalSeconds:F0}s. " +
                    $"Another migration runner appears to be holding it.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    /// <summary>
    /// Stable 64-bit hash so the same key produces the same Postgres advisory-lock id
    /// across processes/architectures. Postgres advisory-lock keys are bigint.
    /// </summary>
    internal static long StableHash64(string key)
    {
        // FNV-1a 64-bit. Postgres advisory locks accept any bigint, including negatives.
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;
        var hash = fnvOffset;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(key))
        {
            hash ^= b;
            hash *= fnvPrime;
        }
        return unchecked((long)hash);
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly NpgsqlConnection _connection;
        private readonly long _lockId;
        private bool _disposed;

        public Lease(NpgsqlConnection connection, long lockId)
        {
            _connection = connection;
            _lockId = lockId;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                await using var cmd = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
                cmd.Parameters.AddWithValue("key", _lockId);
                await cmd.ExecuteScalarAsync();
            }
            catch
            {
                // Connection already closed / aborted — Postgres releases the lock automatically when
                // the session ends, so swallowing here is safe.
            }
        }
    }
}

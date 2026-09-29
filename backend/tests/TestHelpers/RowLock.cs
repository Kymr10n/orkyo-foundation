using Npgsql;

namespace Orkyo.Foundation.Tests;

/// <summary>
/// A writer on its own connection that holds <c>sql</c> uncommitted, for tests of what a
/// concurrent caller does while the row is locked. <see cref="WaitUntilBlockedAsync"/> confirms
/// the caller really waits on this writer before the test commits: a fixed delay let the test
/// pass on a slow machine without the two ever contending.
/// </summary>
public sealed class RowLock : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction _tx;

    private RowLock(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        _conn = conn;
        _tx = tx;
    }

    /// <summary>Runs <paramref name="sql"/> in a transaction that stays open until <see cref="CommitAsync"/>.</summary>
    public static async Task<RowLock> HoldAsync(
        string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        var tx = await conn.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
        return new RowLock(conn, tx);
    }

    /// <summary>
    /// Returns once a backend is waiting on a lock this writer holds. Fails when
    /// <paramref name="pending"/> completes without ever waiting, or when nothing waits within
    /// ten seconds.
    /// </summary>
    public async Task WaitUntilBlockedAsync(Task pending)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            if (pending.IsCompleted)
            {
                await pending; // surfaces its own failure first
                throw new InvalidOperationException("The concurrent call completed without waiting for the held row.");
            }

            await using var cmd = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND @pid = ANY(pg_blocking_pids(pid))",
                _conn, _tx);
            cmd.Parameters.AddWithValue("pid", _conn.ProcessID);
            if ((long)(await cmd.ExecuteScalarAsync())! > 0)
                return;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Nothing blocked on the held row within {Timeout.TotalSeconds}s.");
            await Task.Delay(10);
        }
    }

    public Task CommitAsync() => _tx.CommitAsync();

    public async ValueTask DisposeAsync()
    {
        await _tx.DisposeAsync();
        await _conn.DisposeAsync();
    }
}

using Api.Helpers;
using Npgsql;
using NpgsqlTypes;

namespace Api.Repositories;

/// <summary>
/// Optimistic concurrency on the Postgres system column <c>xmin</c>: every update of a row
/// gives it a new value, so it serves as an opaque row version with no extra column. A write
/// that carries the version its caller read updates only when the row still has that version.
/// </summary>
/// <remarks>
/// The guard sits in the WHERE clause of the write's own UPDATE. Under READ COMMITTED a second
/// writer waits for the first one's row lock, re-reads <c>xmin</c> after the commit, and matches
/// nothing. A null version skips the check (last write wins), which keeps callers that send no
/// <c>If-Match</c> (MCP tools, the Platform API) working unchanged.
/// </remarks>
public static class RowVersion
{
    /// <summary>The parameter name the guard reads.</summary>
    public const string Parameter = "expected_version";

    /// <summary>The WHERE-clause guard for the row aliased <paramref name="alias"/>.</summary>
    public static string Guard(string alias) => $"(@{Parameter} IS NULL OR {alias}.xmin = @{Parameter})";

    /// <summary>Binds the version the guard compares; null disables the check.</summary>
    public static void AddExpectedVersion(this NpgsqlParameterCollection p, uint? expectedVersion)
        => p.AddNullable(Parameter, expectedVersion).NpgsqlDbType = NpgsqlDbType.Xid;

    /// <summary>
    /// For a write that must read the row first: compares in C# what the guard compares in SQL.
    /// The caller locks the row (<c>FOR UPDATE</c>) before the read, so nothing changes after it.
    /// </summary>
    public static void Require(uint? expectedVersion, uint actualVersion, string resourceType)
    {
        if (expectedVersion is { } expected && expected != actualVersion)
            throw new PreconditionFailedException(resourceType);
    }

    /// <summary>
    /// Called when a guarded write matched no row. Throws a 412 when a version was expected and
    /// the row exists; returns otherwise, so the caller keeps its not-found contract.
    /// </summary>
    public static async Task ThrowIfStaleAsync(
        NpgsqlConnection conn, string table, Guid id, uint? expectedVersion, string resourceType, CancellationToken ct)
    {
        if (expectedVersion is not null && await conn.ExistsAsync(table, id, ct))
            throw new PreconditionFailedException(resourceType);
    }
}

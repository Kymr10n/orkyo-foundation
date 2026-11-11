using System.Data;
using System.Runtime.CompilerServices;
using Api.Constants;
using Api.Helpers;
using Api.Services;
using Api.Services.Transfer;
using Npgsql;
using Orkyo.Migrations.Abstractions;

namespace Api.Repositories;

/// <summary>A column of a tenant table as the live schema declares it.</summary>
/// <param name="TypeName">The PostgreSQL type name (<c>udt_name</c>): <c>uuid</c>, <c>text</c>, <c>bytea</c>, …</param>
public sealed record TransferColumn(string Name, string TypeName)
{
    public bool IsBinary => TypeName == "bytea";
}

/// <summary>A row of the tenant's <c>users</c> table, as far as the user merge needs it.</summary>
public sealed record TransferTargetUser(Guid Id, string Email);

/// <summary>The three quota usages an import can change.</summary>
public sealed record TransferUsage(long Sites, long PlaceableResources, long StorageBytes);

/// <summary>
/// Opens the one transaction a tenant export or import runs in. Every statement of a transfer
/// goes through the <see cref="TenantTransferSession"/> it returns, so the export reads one
/// snapshot and the import commits or rolls back as a whole.
/// </summary>
public class TenantTransferRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
{
    public async Task<TenantTransferSession> BeginAsync(IsolationLevel isolation, CancellationToken ct = default)
    {
        var conn = connectionFactory.CreateOrgConnection(orgContext);
        try
        {
            await conn.OpenAsync(ct);
            var tx = await conn.BeginTransactionAsync(isolation, ct);
            return new TenantTransferSession(conn, tx);
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }
}

/// <summary>
/// The SQL of a tenant transfer, on one connection and one transaction. Disposing without
/// <see cref="CommitAsync"/> rolls everything back.
/// <para>
/// Table names come from <see cref="TenantTransferTables"/> and column names from the live
/// schema — never from an archive — so no statement here is built from text a caller supplied.
/// PostgreSQL does the type conversion in both directions (<c>to_jsonb</c> out,
/// <c>jsonb_populate_recordset</c> in), which is why no column type is mapped by hand.
/// </para>
/// </summary>
public sealed class TenantTransferSession(NpgsqlConnection conn, NpgsqlTransaction tx) : IAsyncDisposable
{
    // A table of a large tenant takes longer to erase or load than Npgsql's 30-second default
    // allows. The request's cancellation token still ends a statement when the client goes away.
    private const int StatementTimeoutSeconds = 600;

    // How long an import waits for the tenant's tables before it reports "busy".
    private const string LockTimeout = "10s";

    private const string LockNotAvailable = "55P03";
    private const string DeadlockDetected = "40P01";

    // SQLSTATE classes: 22 is a value its column cannot take, 23 a broken constraint.
    private const string DataException = "22";
    private const string IntegrityConstraintViolation = "23";

    public async ValueTask DisposeAsync()
    {
        await tx.DisposeAsync();
        await conn.DisposeAsync();
    }

    public Task CommitAsync(CancellationToken ct = default) => tx.CommitAsync(ct);

    /// <summary>The ids of the tenant-phase migrations this database has applied.</summary>
    public async Task<IReadOnlyList<string>> ReadJournalIdsAsync(CancellationToken ct = default)
    {
        return await conn.QueryListAsync(
            "SELECT id FROM orkyo_schema_migrations WHERE target_database = @target AND success",
            p => p.AddWithValue("target", nameof(MigrationTargetDatabase.Tenant)),
            r => r.GetString("id"), ct);
    }

    /// <summary>The live columns of every catalog table, in declaration order.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TransferColumn>>> ReadColumnsAsync(
        CancellationToken ct = default)
    {
        var rows = await conn.QueryListAsync(@"
            SELECT table_name, column_name, udt_name
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = ANY(@tables)
            ORDER BY table_name, ordinal_position",
            p => p.AddWithValue("tables", TenantTransferTables.All.Select(t => t.Name).ToArray()),
            r => (Table: r.GetString("table_name"),
                  Column: new TransferColumn(r.GetString("column_name"), r.GetString("udt_name"))),
            ct);
        return rows
            .GroupBy(r => r.Table, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TransferColumn>)g.Select(r => r.Column).ToList(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Every row of <paramref name="table"/> as one JSON object, streamed. A table that
    /// references itself comes out parents first, so a load in the same order never inserts a
    /// child before its parent.
    /// </summary>
    public IAsyncEnumerable<string> ReadRowsAsync(TransferTable table, CancellationToken ct = default)
    {
        var name = Quote(table.Name);
        var sql = table.ParentColumn is { } parent
            ? $@"
                WITH RECURSIVE tree AS (
                    SELECT id, 0 AS depth FROM public.{name} WHERE {Quote(parent)} IS NULL
                    UNION ALL
                    SELECT child.id, tree.depth + 1
                    FROM public.{name} child JOIN tree ON child.{Quote(parent)} = tree.id
                )
                SELECT to_jsonb(t)::text AS row
                FROM public.{name} t JOIN tree ON tree.id = t.id
                ORDER BY tree.depth"
            : $"SELECT to_jsonb(t)::text AS row FROM public.{name} t";
        return ReadRowStreamAsync(sql, ct);
    }

    /// <summary>
    /// The identity columns of the tenant's users, one JSON object each. Keycloak and
    /// lifecycle columns never leave the installation.
    /// </summary>
    public IAsyncEnumerable<string> ReadExportUsersAsync(CancellationToken ct = default) =>
        ReadRowStreamAsync(@"
            SELECT jsonb_build_object(
                'id', id, 'email', email, 'display_name', display_name, 'created_at', created_at)::text AS row
            FROM public.users",
            ct);

    public async Task<IReadOnlyList<TransferTargetUser>> ReadUsersAsync(CancellationToken ct = default)
    {
        return await conn.QueryListAsync(
            "SELECT id, email FROM public.users", bind: null,
            r => new TransferTargetUser(r.GetGuid("id"), r.GetString("email")), ct);
    }

    /// <summary>
    /// Takes the tables an import writes, in a mode that blocks other writers and a second
    /// import while readers keep seeing the data as it was. Returns false when the tables did
    /// not come free in time; the transaction is then unusable.
    /// </summary>
    public async Task<bool> TryLockAsync(CancellationToken ct = default)
    {
        var tables = TenantTransferTables.All
            .Where(t => t.Class != TransferTableClass.Kept)
            .Select(t => $"public.{Quote(t.Name)}");
        try
        {
            await ExecuteAsync($"SET LOCAL lock_timeout = '{LockTimeout}'", ct);
            await ExecuteAsync($"LOCK TABLE {string.Join(", ", tables)} IN EXCLUSIVE MODE", ct);
            return true;
        }
        catch (PostgresException ex) when (ex.SqlState is LockNotAvailable or DeadlockDetected)
        {
            return false;
        }
    }

    /// <summary>
    /// Empties every travelling and erased table, children first. DELETE, not TRUNCATE: it
    /// runs under the app role, it keeps a concurrent export's snapshot intact, and it cannot
    /// reach a table the catalog does not name.
    /// </summary>
    public async Task EraseAsync(CancellationToken ct = default)
    {
        // The two foreign-key cycles need no special step here: in this order the delete of
        // one side either cascades into the other or sets its column to NULL.
        foreach (var table in TenantTransferTables.EraseOrder)
            await ExecuteAsync($"DELETE FROM public.{Quote(table.Name)}", ct);
    }

    /// <summary>
    /// Inserts an archive user that matches nobody on the target, as an account that cannot
    /// sign in. <paramref name="status"/> is set only where the table has the column (Community,
    /// where the tenant's users are the accounts).
    /// </summary>
    public async Task InsertPlaceholderUserAsync(
        Guid id, string email, string displayName, DateTimeOffset createdAt, string metadataJson, string? status,
        CancellationToken ct = default)
    {
        var (statusColumn, statusValue) = status is null ? ("", "") : (", status", ", @status");
        await using var cmd = Command($@"
            INSERT INTO public.users (id, email, display_name, created_at, keycloak_metadata{statusColumn})
            VALUES (@id, @email, @displayName, @createdAt, @metadata{statusValue})
            ON CONFLICT DO NOTHING");
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("createdAt", createdAt.UtcDateTime);
        cmd.Parameters.AddJsonb("metadata", metadataJson);
        if (status is not null) cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a batch of rows given as a JSON array of objects. PostgreSQL converts each
    /// value to its column's type. Returns the number of rows inserted.
    /// </summary>
    public async Task<int> InsertRowsAsync(
        TransferTable table, IReadOnlyList<TransferColumn> columns, string rowsJson, CancellationToken ct = default)
    {
        var name = Quote(table.Name);
        var columnList = string.Join(", ", columns.Select(c => Quote(c.Name)));
        await using var cmd = Command($@"
            INSERT INTO public.{name} ({columnList})
            SELECT {columnList} FROM jsonb_populate_recordset(NULL::public.{name}, @rows)");
        cmd.Parameters.AddJsonb("rows", rowsJson);
        return await ExecuteLoadAsync(cmd, table, ct);
    }

    /// <summary>
    /// Sets a deferred column once every table is loaded. <paramref name="pairsJson"/> is a
    /// JSON array of <c>{"id": …, "value": …}</c>.
    /// </summary>
    public async Task<int> SetDeferredColumnAsync(TransferTable table, string pairsJson, CancellationToken ct = default)
    {
        await using var cmd = Command($@"
            UPDATE public.{Quote(table.Name)} AS t
            SET {Quote(table.DeferredColumn!)} = v.value
            FROM jsonb_to_recordset(@pairs) AS v(id uuid, value uuid)
            WHERE t.id = v.id");
        cmd.Parameters.AddJsonb("pairs", pairsJson);
        return await ExecuteLoadAsync(cmd, table, ct);
    }

    /// <summary>
    /// Runs a statement that writes archive rows. An import skips the request validators, so
    /// the database's own rules are the check on what an archive holds: a row that breaks a
    /// constraint, or a value its column cannot take, is the archive's fault and is reported
    /// as such rather than as a server error.
    /// </summary>
    private static async Task<int> ExecuteLoadAsync(NpgsqlCommand cmd, TransferTable table, CancellationToken ct)
    {
        try
        {
            return await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (PostgresException ex) when (
            ex.SqlState.StartsWith(DataException, StringComparison.Ordinal)
            || ex.SqlState.StartsWith(IntegrityConstraintViolation, StringComparison.Ordinal))
        {
            throw new TransferRejectedException(422, ApiErrorCodes.TransferArchiveInvalid,
                $"The archive's rows for '{table.Name}' break a rule of this installation's data" +
                (ex.ConstraintName is { Length: > 0 } constraint ? $" ({constraint})." : "."));
        }
    }

    /// <summary>
    /// The quota usages as this transaction sees them. The query texts are the ones the site,
    /// resource and asset repositories use, so an import and a normal write count alike.
    /// </summary>
    public async Task<TransferUsage> ReadUsageAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var sites = Command(SiteRepository.CountSql);
        await using var placeable = Command(ResourceRepository.PlaceableCountSql);
        await using var storage = Command(AssetRepository.TotalSizeBytesSql);
        storage.Parameters.AddWithValue("tenantId", tenantId);

        return new TransferUsage(
            Convert.ToInt64(await sites.ExecuteScalarAsync(ct)),
            Convert.ToInt64(await placeable.ExecuteScalarAsync(ct)),
            Convert.ToInt64(await storage.ExecuteScalarAsync(ct)));
    }

    private NpgsqlCommand Command(string sql) =>
        new(sql, conn, tx) { CommandTimeout = StatementTimeoutSeconds };

    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var cmd = Command(sql);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async IAsyncEnumerable<string> ReadRowStreamAsync(
        string sql, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var cmd = Command(sql);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return reader.GetString("row");
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}

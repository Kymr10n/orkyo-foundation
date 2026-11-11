using System.Data;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.Configuration;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security.Encryption;
using Api.Security.Quotas;

namespace Api.Services.Transfer;

/// <summary>
/// Moves a complete tenant dataset out of this installation as an archive, and loads an
/// archive into the current tenant in place of its data.
/// <para>
/// The export reads one snapshot. The import runs in one transaction: lock, erase, merge the
/// users, load, check the row counts and the quotas, commit. Anything that fails before the
/// commit leaves the tenant exactly as it was.
/// </para>
/// </summary>
public sealed class TenantTransferService(
    TenantTransferRepository repository,
    TenantTransferVersionGate gate,
    IEncryptionService encryption,
    OrgContext org,
    IQuotaEnforcer quotas,
    IQuotaUsageRollup rollup,
    ITenantUserService audit,
    DeploymentConfig deployment,
    TimeProvider time)
{
    // A batch is one INSERT. Rows are small and floorplans are large, so a batch closes on
    // whichever limit comes first.
    private const int BatchRows = 500;
    private const int BatchChars = 8 * 1024 * 1024;

    private const string PlaceholderEmailDomain = "invalid.orkyo";

    // The user list is held in memory for the merge. No tenant comes near this; a crafted
    // archive must not be able to make the list as large as the upload allows.
    private const int MaxArchiveUsers = 100_000;

    // Rows keep the characters PostgreSQL wrote; nothing here is HTML.
    private static readonly JsonSerializerOptions RowJson = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The upload ceiling in bytes. An installation that has not set it has not sized its
    /// ingress for an import, so the import routes refuse rather than guess a limit.
    /// </summary>
    public long MaxArchiveBytes =>
        deployment.TransferMaxArchiveBytes
        ?? throw new TransferRejectedException(503, ApiErrorCodes.TransferNotConfigured,
            $"Import is not configured on this installation: {Orkyo.Shared.ConfigKeys.TransferMaxArchiveBytes} is not set.");

    // ── Export ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the tenant's archive to <paramref name="destination"/> and returns the file
    /// name to offer. The archive holds plain text: values that are encrypted at rest are
    /// decrypted, because their keys are bound to this installation and this tenant.
    /// </summary>
    public async Task<string> ExportAsync(Stream destination, Guid? actorUserId, CancellationToken ct = default)
    {
        var exportedAt = time.GetUtcNow();

        // One snapshot for every table: a write that lands mid-export is either in all of
        // the archive or in none of it.
        await using var session = await repository.BeginAsync(IsolationLevel.RepeatableRead, ct);
        ThrowIfBlocked(gate.CheckDatabase(await session.ReadJournalIdsAsync(ct)));
        var columns = await session.ReadColumnsAsync(ct);

        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var encryptedColumns = new SortedSet<string>(StringComparer.Ordinal);

        using (var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            counts[TenantTransferTables.Users] = await TenantArchive.WriteLinesAsync(
                zip, TenantArchive.UsersEntry, session.ReadExportUsersAsync(ct), ct);

            foreach (var table in TenantTransferTables.Travelling)
            {
                var binaryColumns = BinaryColumns(columns, table);
                var rows = session.ReadRowsAsync(table, ct)
                    .Select(row => Decrypt(table, row, binaryColumns, encryptedColumns));
                counts[table.Name] = await TenantArchive.WriteLinesAsync(zip, TenantArchive.TableEntry(table), rows, ct);
            }

            await TenantArchive.WriteManifestAsync(zip, new TransferManifest
            {
                FormatVersion = TransferManifest.CurrentFormatVersion,
                SourceEdition = deployment.DeploymentMode,
                AppVersion = deployment.Version,
                ExportedAt = exportedAt,
                SourceTenantId = org.OrgId,
                SourceTenantSlug = org.OrgSlug,
                SchemaVersion = gate.Current,
                Tables = counts,
                EncryptedColumns = encryptedColumns.ToList(),
            }, ct);
        }

        await audit.RecordAuditEventAsync(org, TenantAuditActions.TransferExported, actorUserId,
            metadata: new { rows = counts.Values.Sum() }, ct: ct);

        return $"orkyo-transfer-{org.OrgSlug}-{exportedAt:yyyyMMdd-HHmmss}.orkyoexport";
    }

    private string Decrypt(
        TransferTable table, string row, IReadOnlySet<string> binaryColumns, ISet<string> encryptedColumns)
    {
        var values = JsonNode.Parse(row)!.AsObject();
        foreach (var (column, value) in values.ToList())
        {
            if (value is not JsonValue json || !json.TryGetValue<string>(out var text)) continue;

            if (binaryColumns.Contains(column))
            {
                var stored = FromHex(text);
                if (!encryption.IsProtected(stored)) continue;
                values[column] = ToHex(encryption.UnprotectBytes(stored, org.OrgId));
            }
            else
            {
                if (!encryption.IsProtected(text)) continue;
                values[column] = encryption.UnprotectString(text, org.OrgId);
            }
            encryptedColumns.Add($"{table.Name}.{column}");
        }
        return values.ToJsonString(RowJson);
    }

    // ── Validate ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reports whether <paramref name="archive"/> can be imported here and what the import
    /// would do to the users. Changes nothing. An archive that is not a transfer archive at
    /// all is refused outright rather than reported as a blocker.
    /// </summary>
    public async Task<TransferValidationResult> ValidateAsync(Stream archive, CancellationToken ct = default)
    {
        using var reader = TenantArchive.Open(archive, MaxArchiveBytes);
        var manifest = await reader.ReadManifestAsync(ct);

        await using var session = await repository.BeginAsync(IsolationLevel.ReadCommitted, ct);
        var blockers = new[]
            {
                gate.CheckDatabase(await session.ReadJournalIdsAsync(ct)),
                gate.CheckArchive(manifest.SchemaVersion),
            }
            .OfType<TransferBlocker>()
            .ToList();

        var users = TransferUserMatcher.Match(
            await ReadArchiveUsersAsync(reader, ct), await session.ReadUsersAsync(ct));

        return new TransferValidationResult
        {
            SourceEdition = manifest.SourceEdition,
            AppVersion = manifest.AppVersion,
            ExportedAt = manifest.ExportedAt,
            SourceTenantSlug = manifest.SourceTenantSlug,
            Blockers = blockers,
            Tables = manifest.Tables,
            MatchedUsers = users.Matched,
            UnmatchedEmails = users.Placeholders.Select(u => u.Email).Order(StringComparer.Ordinal).ToList(),
        };
    }

    // ── Import ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the tenant's data with <paramref name="archive"/>.
    /// <paramref name="confirmation"/> must be the tenant's slug: the caller typed it to
    /// confirm that the current data goes away.
    /// </summary>
    public async Task<TransferImportResult> ImportAsync(
        Stream archive, string? confirmation, Guid? actorUserId, CancellationToken ct = default)
    {
        if (!string.Equals(confirmation, org.OrgSlug, StringComparison.Ordinal))
            throw new ArgumentException("The confirmation does not match this organization's identifier.");

        using var reader = TenantArchive.Open(archive, MaxArchiveBytes);
        var manifest = await reader.ReadManifestAsync(ct);
        var encryptedColumns = EncryptedColumnsByTable(manifest);

        await using var session = await repository.BeginAsync(IsolationLevel.ReadCommitted, ct);
        ThrowIfBlocked(gate.CheckDatabase(await session.ReadJournalIdsAsync(ct)));
        ThrowIfBlocked(gate.CheckArchive(manifest.SchemaVersion));

        if (!await session.TryLockAsync(ct))
            throw new TransferRejectedException(409, ApiErrorCodes.TransferBusy,
                "Another import or a long-running change holds this organization's data. Try again in a moment.");

        var columns = await session.ReadColumnsAsync(ct);
        var usageBefore = await session.ReadUsageAsync(org.OrgId, ct);

        await session.EraseAsync(ct);

        var users = TransferUserMatcher.Match(
            await ReadArchiveUsersAsync(reader, ct), await session.ReadUsersAsync(ct));
        await InsertPlaceholdersAsync(session, users.Placeholders, columns, ct);

        var loaded = TenantTransferTables.Travelling.ToDictionary(t => t.Name, _ => 0L);
        var deferred = new Dictionary<TransferTable, List<(string Id, string Value)>>();
        TableLoad LoadOf(TransferTable table, List<(string Id, string Value)>? deferredValues) => new(
            table, columns[table.Name], users.IdMap,
            encryptedColumns.GetValueOrDefault(table.Name) ?? [], deferredValues);

        foreach (var table in TenantTransferTables.Travelling)
        {
            var load = LoadOf(table, table.DeferredColumn is null ? null : deferred[table] = []);
            loaded[table.Name] += await LoadTableAsync(session, reader, load, lateRows: false, ct);

            // The rows of an earlier table that point at this one go in now.
            foreach (var earlier in TenantTransferTables.Travelling.Where(t => t.LateAfter == table.Name))
                loaded[earlier.Name] += await LoadTableAsync(session, reader, LoadOf(earlier, null), lateRows: true, ct);
        }

        foreach (var (table, count) in loaded)
        {
            if (count != manifest.Tables.GetValueOrDefault(table, -1))
                throw TenantArchive.Invalid($"The archive's row count for '{table}' does not match its manifest.");
        }
        var rows = loaded.Values.Sum();

        foreach (var (table, pairs) in deferred)
            await SetDeferredColumnAsync(session, table, pairs, ct);

        var usageAfter = await session.ReadUsageAsync(org.OrgId, ct);
        var usages = QuotaChanges(usageBefore, usageAfter);
        foreach (var (key, before, after) in usages.Where(u => u.After > u.Before))
            await quotas.EnsureWithinLimitAsync(key, before, after - before, ct);

        await session.CommitAsync(ct);

        // After the commit, like every other write: the rollup lives in the control plane and
        // must not report data that a rollback then takes away. From here the data is in, so
        // the bookkeeping no longer follows the request: a caller that has gone away must not
        // leave the usage wrong or the import unrecorded.
        foreach (var (key, before, after) in usages.Where(u => u.After != u.Before))
            await rollup.RecordDeltaAsync(key, after - before, CancellationToken.None);

        var result = new TransferImportResult
        {
            Rows = rows,
            MatchedUsers = users.Matched,
            PlaceholderUsers = users.Placeholders.Count,
        };
        await audit.RecordAuditEventAsync(org, TenantAuditActions.TransferImported, actorUserId,
            metadata: new
            {
                sourceEdition = manifest.SourceEdition,
                sourceAppVersion = manifest.AppVersion,
                sourceTenantSlug = manifest.SourceTenantSlug,
                rows = result.Rows,
                matchedUsers = result.MatchedUsers,
                placeholderUsers = result.PlaceholderUsers,
            }, ct: CancellationToken.None);
        return result;
    }

    private static (string Key, long Before, long After)[] QuotaChanges(TransferUsage before, TransferUsage after) =>
    [
        (QuotaResourceTypes.ProductionSites, before.Sites, after.Sites),
        (QuotaResourceTypes.Spaces, before.PlaceableResources, after.PlaceableResources),
        (QuotaResourceTypes.StorageBytes, before.StorageBytes, after.StorageBytes),
    ];

    private async Task InsertPlaceholdersAsync(
        TenantTransferSession session,
        IReadOnlyList<TransferArchiveUser> placeholders,
        IReadOnlyDictionary<string, IReadOnlyList<TransferColumn>> columns,
        CancellationToken ct)
    {
        // Community keeps its accounts in this table, so a placeholder there must also be an
        // account that cannot sign in. A per-tenant database has no status column to set.
        var status = columns[TenantTransferTables.Users].Any(c => c.Name == "status")
            ? UserStatusConstants.Disabled
            : null;

        foreach (var user in placeholders)
        {
            var metadata = new JsonObject
            {
                ["transfer_placeholder"] = true,
                ["original_email"] = user.Email,
            };
            await session.InsertPlaceholderUserAsync(
                user.Id,
                $"transfer-placeholder+{user.Id:N}@{PlaceholderEmailDomain}",
                string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName,
                user.CreatedAt,
                metadata.ToJsonString(),
                status,
                ct);
        }
    }

    private sealed record TableLoad(
        TransferTable Table,
        IReadOnlyList<TransferColumn> Columns,
        IReadOnlyDictionary<Guid, Guid> UserIds,
        IReadOnlySet<string> EncryptedColumns,
        List<(string Id, string Value)>? Deferred);

    /// <summary>
    /// Loads one table's rows. A table with a late column is read twice: once for the rows
    /// that leave the column empty, and once — <paramref name="lateRows"/> — for the rest.
    /// </summary>
    private async Task<long> LoadTableAsync(
        TenantTransferSession session, TenantArchive.Reader reader, TableLoad load, bool lateRows,
        CancellationToken ct)
    {
        long loaded = 0;
        var batch = new StringBuilder("[");
        var batchRows = 0;

        async Task FlushAsync()
        {
            if (batchRows == 0) return;
            batch.Append(']');
            loaded += await session.InsertRowsAsync(load.Table, load.Columns, batch.ToString(), ct);
            batch.Clear().Append('[');
            batchRows = 0;
        }

        await foreach (var line in reader.ReadLinesAsync(TenantArchive.TableEntry(load.Table), ct))
        {
            var values = Parse(load, line);
            if (load.Table.LateColumn is { } late && (values[late] is not null) != lateRows) continue;

            if (batchRows > 0) batch.Append(',');
            batch.Append(Prepare(load, values));
            batchRows++;
            if (batchRows >= BatchRows || batch.Length >= BatchChars) await FlushAsync();
        }
        await FlushAsync();
        return loaded;
    }

    /// <summary>One archive line as a row, refused unless it has exactly the target's columns.</summary>
    private static JsonObject Parse(TableLoad load, string line)
    {
        JsonObject? values;
        try
        {
            values = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            values = null;
        }
        return values is not null && values.Count == load.Columns.Count && load.Columns.All(c => values.ContainsKey(c.Name))
            ? values
            : throw TenantArchive.Invalid($"A row of '{load.Table.Name}' does not have the columns this version stores.");
    }

    /// <summary>
    /// Turns one archive row into the row the target stores: user ids as the target knows
    /// them, the tenant id of the target, values encrypted under the target, and the
    /// cycle-closing column held back.
    /// </summary>
    private string Prepare(TableLoad load, JsonObject values)
    {
        var table = load.Table;
        foreach (var column in table.UserColumns)
        {
            if (!TryGetString(values, column, out var raw)) continue;
            values[column] = Guid.TryParse(raw, out var id) && load.UserIds.TryGetValue(id, out var mapped)
                ? mapped.ToString()
                : throw TenantArchive.Invalid($"A row of '{table.Name}' names a user the archive does not contain.");
        }

        if (table.Name == TenantTransferTables.Assets)
            values["tenant_id"] = org.OrgId.ToString();

        foreach (var column in load.Columns.Where(c => load.EncryptedColumns.Contains(c.Name)))
        {
            if (!TryGetString(values, column.Name, out var plain)) continue;
            if (column.IsBinary)
            {
                values[column.Name] = ToHex(encryption.ProtectBytes(FromHex(plain), org.OrgId));
                if (table.Name == TenantTransferTables.Assets)
                {
                    // The envelope a floorplan now has is this installation's, not the source's.
                    values["enc_algorithm"] = encryption.Algorithm;
                    values["enc_key_version"] = encryption.KeyVersion;
                }
            }
            else
            {
                values[column.Name] = encryption.ProtectString(plain, org.OrgId);
            }
        }

        if (load.Deferred is not null && TryGetString(values, table.DeferredColumn!, out var target))
        {
            if (!TryGetString(values, "id", out var id))
                throw TenantArchive.Invalid($"A row of '{table.Name}' has no id.");
            load.Deferred.Add((id, target));
            values[table.DeferredColumn!] = null;
        }

        return values.ToJsonString(RowJson);
    }

    private static async Task SetDeferredColumnAsync(
        TenantTransferSession session, TransferTable table, List<(string Id, string Value)> pairs, CancellationToken ct)
    {
        foreach (var chunk in pairs.Chunk(BatchRows))
        {
            var json = new JsonArray([.. chunk.Select(p => new JsonObject { ["id"] = p.Id, ["value"] = p.Value })]);
            var updated = await session.SetDeferredColumnAsync(table, json.ToJsonString(), ct);
            if (updated != chunk.Length)
                throw TenantArchive.Invalid($"A row of '{table.Name}' could not be linked.");
        }
    }

    private static async Task<List<TransferArchiveUser>> ReadArchiveUsersAsync(
        TenantArchive.Reader reader, CancellationToken ct)
    {
        var users = new List<TransferArchiveUser>();
        await foreach (var line in reader.ReadLinesAsync(TenantArchive.UsersEntry, ct))
        {
            TransferArchiveUser? user;
            try
            {
                user = JsonSerializer.Deserialize<TransferArchiveUser>(line, TransferArchiveUser.Json);
            }
            catch (JsonException)
            {
                user = null;
            }
            if (user is null || user.Id == Guid.Empty || string.IsNullOrWhiteSpace(user.Email)
                || users.Count == MaxArchiveUsers)
                throw TenantArchive.Invalid("The archive's user list is not valid.");
            users.Add(user);
        }
        return users;
    }

    /// <summary>
    /// The manifest's <c>table.column</c> list, per table. The list is the archive's claim, so
    /// every entry must name a table that travels. A column the table does not have is never
    /// looked up, and a column that cannot hold an envelope fails its insert.
    /// </summary>
    private static Dictionary<string, HashSet<string>> EncryptedColumnsByTable(TransferManifest manifest)
    {
        var travelling = TenantTransferTables.Travelling.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        var byTable = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var entry in manifest.EncryptedColumns)
        {
            var parts = entry?.Split('.') ?? [];
            if (parts.Length != 2 || !travelling.Contains(parts[0]))
                throw TenantArchive.Invalid("The archive manifest is not valid.");
            if (!byTable.TryGetValue(parts[0], out var columns))
                byTable[parts[0]] = columns = new HashSet<string>(StringComparer.Ordinal);
            columns.Add(parts[1]);
        }
        return byTable;
    }

    private static HashSet<string> BinaryColumns(
        IReadOnlyDictionary<string, IReadOnlyList<TransferColumn>> columns, TransferTable table) =>
        columns[table.Name].Where(c => c.IsBinary).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    private static bool TryGetString(JsonObject values, string column, out string value)
    {
        value = "";
        return values[column] is JsonValue json && json.TryGetValue(out value!);
    }

    private static void ThrowIfBlocked(TransferBlocker? blocker)
    {
        if (blocker is not null)
            throw new TransferRejectedException(422, blocker.Code, blocker.Message);
    }

    // PostgreSQL writes bytea into JSON in its hex form: \x followed by two digits per byte.
    private const string HexPrefix = "\\x";

    private static byte[] FromHex(string text)
    {
        try
        {
            return text.StartsWith(HexPrefix, StringComparison.Ordinal)
                ? Convert.FromHexString(text.AsSpan(HexPrefix.Length))
                : throw new FormatException();
        }
        catch (FormatException)
        {
            throw TenantArchive.Invalid("A binary value in the archive is not valid.");
        }
    }

    private static string ToHex(byte[] bytes) => HexPrefix + Convert.ToHexString(bytes);
}

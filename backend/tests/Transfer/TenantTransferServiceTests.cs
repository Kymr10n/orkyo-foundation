using System.IO.Compression;
using System.Text.Json.Nodes;
using Api.Helpers;
using Api.Security.Quotas;
using Api.Services.Transfer;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Foundation.Tests.Integration;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Tests.Transfer;

/// <summary>
/// The tenant transfer against two real tenant databases: what an import carries across,
/// what it leaves alone, and that every refusal leaves the target as it was.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection(PostgresCollection.Name)]
public class TenantTransferServiceTests(TenantTransferFixture fixture) : IClassFixture<TenantTransferFixture>
{
    private static readonly Dictionary<Guid, Guid> ExpectedUserIds = new()
    {
        [TenantTransferFixture.SameIdUser] = TenantTransferFixture.SameIdUser,
        [TenantTransferFixture.EmailUserOnSource] = TenantTransferFixture.EmailUserOnTarget,
        [TenantTransferFixture.SourceOnlyUser] = TenantTransferFixture.SourceOnlyUser,
    };

    private MemoryStream Archive() => new(fixture.Archive);

    private Task<Api.Models.TransferImportResult> ImportAsync(Stream archive, TenantTransferService? service = null) =>
        (service ?? fixture.CreateService()).ImportAsync(archive, TenantTransferFixture.TargetSlug, actorUserId: null);

    // ── Round trip ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_LoadsEveryTravellingTableAsTheSourceHoldsIt()
    {
        await fixture.ResetTargetAsync();

        var result = await ImportAsync(Archive());

        long rows = 0;
        foreach (var table in TenantTransferTables.Travelling)
        {
            var source = await DumpAsync(source: true, table);
            var target = await DumpAsync(source: false, table);
            source.Should().NotBeNull();
            target.Should().Equal(source, $"'{table.Name}' holds the same rows on both sides");
            rows += source.Count;
        }
        result.Rows.Should().Be(rows);
        rows.Should().BeGreaterThan(500, "the seeded source is a whole shop, not a handful of rows");

        // The tables the round trip is only meaningful with must actually hold data.
        foreach (var table in new[] { "requests", "list_rows", "list_instances", "resource_custom_fields", "assets", "memberships", "resource_scan_codes" })
            (await fixture.CountAsync(source: false, table)).Should().BeGreaterThan(0, $"the seed fills '{table}'");
        (await fixture.QueryAsync(source: false, "SELECT COUNT(*) FROM requests WHERE parent_request_id IS NOT NULL"))
            .Single().Should().NotBe("0", "the seed builds request trees, so the parents-first order is exercised");
        (await fixture.QueryAsync(source: false, "SELECT COUNT(*) FROM resource_custom_fields WHERE list_instance_id IS NOT NULL"))
            .Single().Should().NotBe("0", "the seed binds a field to a shared list, so the deferred column is exercised");
    }

    [Fact]
    public async Task Import_RebuildsTheSearchIndex_AndRecordsAnAuditEvent()
    {
        await fixture.ResetTargetAsync();

        await ImportAsync(Archive());

        (await fixture.CountAsync(source: false, "search_documents"))
            .Should().Be(await fixture.CountAsync(source: true, "search_documents"));
        (await fixture.QueryAsync(source: false, "SELECT action FROM audit_events ORDER BY created_at"))
            .Should().Equal(["target.kept", TenantAuditActions.TransferImported]);
    }

    [Fact]
    public async Task Import_ErasesWhatWasThere_AndLeavesTheKeptTablesAlone()
    {
        await fixture.ResetTargetAsync();

        await ImportAsync(Archive());

        (await fixture.QueryAsync(source: false, "SELECT COUNT(*) FROM sites WHERE code = 'OLD'")).Single().Should().Be("0");
        (await fixture.CountAsync(source: false, "calendar_feed_tokens"))
            .Should().Be(0, "the target's tokens are erased and the source's never travel");

        (await fixture.QueryAsync(source: false, "SELECT value FROM tenant_settings")).Should().Equal(["Target Shop"]);
        (await fixture.QueryAsync(source: false, "SELECT title FROM ai_conversations")).Should().Equal(["Kept chat"]);
        (await fixture.CountAsync(source: false, "user_preferences")).Should().Be(1);
        (await fixture.QueryAsync(source: false, "SELECT action FROM audit_events")).Should().NotContain("source.only");
    }

    // ── Users ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_MapsUsersByIdThenEmail_AndMakesAPlaceholderOfTheRest()
    {
        await fixture.ResetTargetAsync();

        var result = await ImportAsync(Archive());

        result.MatchedUsers.Should().Be(2);
        result.PlaceholderUsers.Should().Be(1);

        // The two matched users are untouched — the id match kept the target's newer email.
        (await fixture.QueryAsync(source: false, "SELECT email FROM users ORDER BY email")).Should().Equal(
        [
            "MATCH@both.test",
            "only@target.test",
            "renamed@target.test",
            $"transfer-placeholder+{TenantTransferFixture.SourceOnlyUser:N}@invalid.orkyo",
        ]);

        var placeholder = (await fixture.QueryAsync(source: false,
            $"SELECT display_name || '|' || keycloak_metadata::text FROM users WHERE id = '{TenantTransferFixture.SourceOnlyUser}'")).Single();
        placeholder.Should().StartWith($"{TenantTransferFixture.SourceOnlyEmail}|", "a user with no name is shown by the email they had");
        JsonNode.Parse(placeholder.Split('|', 2)[1])!["original_email"]!.GetValue<string>()
            .Should().Be(TenantTransferFixture.SourceOnlyEmail);

        // Data that named the email-matched user now names that user's id on the target.
        (await fixture.QueryAsync(source: false, "SELECT user_id FROM memberships")).Should()
            .Equal([TenantTransferFixture.EmailUserOnTarget.ToString()]);
        (await fixture.QueryAsync(source: false, "SELECT created_by_user_id FROM resource_scan_codes")).Should()
            .Equal([TenantTransferFixture.SourceOnlyUser.ToString()], "a column with no foreign key is rewritten too");
    }

    [Fact]
    public async Task Import_ASecondTime_FindsItsPlaceholderById()
    {
        await fixture.ResetTargetAsync();
        await ImportAsync(Archive());

        var second = await ImportAsync(Archive());

        second.MatchedUsers.Should().Be(3);
        second.PlaceholderUsers.Should().Be(0);
        (await fixture.CountAsync(source: false, "users")).Should().Be(4);
    }

    [Fact]
    public async Task Validate_ReportsTheUserMatch_WithoutChangingAnything()
    {
        await fixture.ResetTargetAsync();

        var result = await fixture.CreateService().ValidateAsync(Archive());

        result.Blockers.Should().BeEmpty();
        result.MatchedUsers.Should().Be(2);
        result.UnmatchedEmails.Should().Equal([TenantTransferFixture.SourceOnlyEmail]);
        result.SourceTenantSlug.Should().Be(TenantTransferFixture.SourceSlug);
        result.SourceEdition.Should().Be("self-hosted");
        result.AppVersion.Should().Be("9.9.9-test");
        result.Tables["requests"].Should().Be(await fixture.CountAsync(source: true, "requests"));
        (await fixture.CountAsync(source: false, "users")).Should().Be(3);
        (await fixture.QueryAsync(source: false, "SELECT code FROM sites")).Should().Equal(["OLD"]);
    }

    // ── Encryption ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_HoldsPlainText_AndTheTargetStoresItUnderItsOwnTenant()
    {
        await fixture.ResetTargetAsync();

        using (var zip = new ZipArchive(Archive(), ZipArchiveMode.Read))
        {
            TenantTransferFixture.ReadEntry(zip, "tables/resources.ndjson").Should().Contain(TenantTransferFixture.SecretNote);
            TenantTransferFixture.ReadEntry(zip, "tables/sites.ndjson").Should().Contain(TenantTransferFixture.SecretAddress);
            TenantTransferFixture.ReadEntry(zip, "tables/assets.ndjson").Should().NotContain("\"data\"");
            zip.Entries.Where(e => e.FullName.StartsWith("blobs/assets/", StringComparison.Ordinal))
                .Select(e => TenantTransferFixture.ReadEntry(zip, e.FullName))
                .Should().Contain(System.Text.Encoding.UTF8.GetString(TenantTransferFixture.SecretFloorplan));
        }

        await ImportAsync(Archive());

        var note = (await fixture.QueryAsync(source: false, "SELECT notes FROM resources WHERE notes IS NOT NULL")).Single();
        fixture.Encryption.IsProtected(note).Should().BeTrue("the value is encrypted at rest again");
        fixture.Encryption.UnprotectString(note, TenantTransferFixture.TargetTenantId).Should().Be(TenantTransferFixture.SecretNote);

        // Every address is encrypted now, also the ones the source held as plain text.
        var addresses = await fixture.QueryAsync(source: false, "SELECT address FROM sites WHERE address IS NOT NULL");
        addresses.Should().OnlyContain(a => fixture.Encryption.IsProtected(a));
        addresses.Select(a => fixture.Encryption.UnprotectString(a, TenantTransferFixture.TargetTenantId))
            .Should().Contain(TenantTransferFixture.SecretAddress);

        var floorplan = Convert.FromHexString((await fixture.QueryAsync(source: false,
            $"SELECT encode(data, 'hex') FROM assets WHERE size_bytes = {TenantTransferFixture.SecretFloorplan.Length}")).Single());
        fixture.Encryption.UnprotectBytes(floorplan, TenantTransferFixture.TargetTenantId)
            .Should().Equal(TenantTransferFixture.SecretFloorplan);
        (await fixture.QueryAsync(source: false, "SELECT DISTINCT tenant_id FROM assets")).Should()
            .Equal([TenantTransferFixture.TargetTenantId.ToString()]);

        // The source also held floorplans that were never encrypted. They arrive encrypted,
        // with the metadata that says so.
        (await fixture.QueryAsync(source: true, "SELECT COUNT(*) FROM assets WHERE enc_algorithm IS NULL"))
            .Single().Should().NotBe("0");
        (await fixture.QueryAsync(source: false,
            "SELECT COUNT(*) FROM assets WHERE enc_algorithm IS NULL OR substring(data from 1 for 4) <> 'ORK1'::bytea"))
            .Single().Should().Be("0");
    }

    [Fact]
    public async Task TextThatLooksEncrypted_InAColumnTheApplicationDoesNotEncrypt_TravelsAsItIs()
    {
        await fixture.ResetTargetAsync();
        const string name = "orkyoenc:not an envelope";
        await fixture.QueryAsync(source: false, $"UPDATE sites SET name = '{name}' RETURNING id");

        using var archive = new MemoryStream();
        await fixture.CreateService().ExportAsync(archive, actorUserId: null);
        archive.Position = 0;
        await ImportAsync(archive);

        (await fixture.QueryAsync(source: false, "SELECT name FROM sites")).Should().Equal([name]);
    }

    [Fact]
    public async Task Export_OfRequestsWhoseParentsFormARing_FailsInsteadOfLeavingThemOut()
    {
        await fixture.ResetTargetAsync();
        await ImportAsync(Archive());
        // The application refuses this; only a direct write can produce it.
        await fixture.QueryAsync(source: false, @"
            WITH pair AS (SELECT (array_agg(id ORDER BY id))[1] AS a, (array_agg(id ORDER BY id))[2] AS b
                          FROM requests WHERE parent_request_id IS NULL)
            UPDATE requests r SET parent_request_id = CASE WHEN r.id = pair.a THEN pair.b ELSE pair.a END
            FROM pair WHERE r.id IN (pair.a, pair.b) RETURNING r.id");

        var act = () => fixture.CreateService().ExportAsync(new MemoryStream(), actorUserId: null);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*requests*ring*");
    }

    // ── Version gate ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("older", "older schema version")]
    [InlineData("newer", "newer schema version")]
    [InlineData("different", "different schema version")]
    public async Task Import_RefusesAnArchiveOfAnotherSchemaVersion(string kind, string expectedMessage)
    {
        await fixture.ResetTargetAsync();
        using var archive = fixture.DoctoredArchive(zip =>
        {
            var manifest = JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, TenantArchive.ManifestEntry))!.AsObject();
            var version = manifest["schemaVersion"]!.AsArray();
            if (kind is "older" or "different") version.RemoveAt(version.Count - 1);
            if (kind is "newer" or "different") version.Add("9999.foundation.from_the_future");
            TenantTransferFixture.ReplaceEntry(zip, TenantArchive.ManifestEntry, manifest.ToJsonString());
        });

        var act = () => ImportAsync(archive);

        var rejection = (await act.Should().ThrowAsync<TransferRejectedException>()).Which;
        rejection.Code.Should().Be(ApiErrorCodes.TransferVersionMismatch);
        rejection.StatusCode.Should().Be(422);
        rejection.Message.Should().Contain(expectedMessage);
        await AssertTargetUntouchedAsync();

        archive.Position = 0;
        (await fixture.CreateService().ValidateAsync(archive)).Blockers.Should().ContainSingle()
            .Which.Code.Should().Be(ApiErrorCodes.TransferVersionMismatch);
    }

    [Fact]
    public async Task ExportAndImport_RefuseATenantDatabaseThatIsBehindTheApplication()
    {
        await fixture.ResetTargetAsync();
        // A build that knows one more tenant migration than either database has applied.
        IMigrationModule[] ahead = [new FoundationMigrationModule(), new OneMoreMigration()];

        var export = () => fixture.CreateService(source: true, modules: ahead).ExportAsync(new MemoryStream(), null);
        var import = () => ImportAsync(Archive(), fixture.CreateService(modules: ahead));

        (await export.Should().ThrowAsync<TransferRejectedException>()).Which.Code.Should().Be(ApiErrorCodes.TransferSchemaBehind);
        (await import.Should().ThrowAsync<TransferRejectedException>()).Which.Code.Should().Be(ApiErrorCodes.TransferSchemaBehind);
        await AssertTargetUntouchedAsync();
    }

    private sealed class OneMoreMigration : IMigrationModule
    {
        public string ModuleName => "foundation";
        public int Order => 1001;
        public IReadOnlyCollection<MigrationScript> GetMigrations() =>
            [new("9999.foundation.not_applied_yet", "foundation", MigrationTargetDatabase.Tenant, "SELECT 1", "x", [])];
    }

    // ── Archive rules ────────────────────────────────────────────────────────

    public static TheoryData<string> BrokenArchives => new(
        "not-a-zip", "extra-entry", "missing-entry", "bad-manifest", "format-version",
        "row-with-extra-column", "row-count", "unknown-user", "bad-user-list",
        "duplicate-entry", "oversized-manifest", "binary-value-no-row-names", "row-without-its-binary-value", "row-that-breaks-a-foreign-key", "value-of-the-wrong-type",
        "manifest-with-null-tables", "row-that-is-not-json", "row-too-large", "binary-row-without-id",
        "deferred-row-without-id");

    [Theory]
    [MemberData(nameof(BrokenArchives))]
    public async Task Import_RefusesAnArchiveThatBreaksAFormatRule_AndChangesNothing(string kind)
    {
        await fixture.ResetTargetAsync();
        using var archive = kind == "not-a-zip"
            ? new MemoryStream("this is not an archive"u8.ToArray())
            : fixture.DoctoredArchive(zip => Break(zip, kind));

        var act = () => ImportAsync(archive);

        var rejection = (await act.Should().ThrowAsync<TransferRejectedException>()).Which;
        rejection.Code.Should().Be(ApiErrorCodes.TransferArchiveInvalid);
        rejection.StatusCode.Should().Be(422);
        await AssertTargetUntouchedAsync();
    }

    private static void Break(ZipArchive zip, string kind)
    {
        JsonObject Manifest() => JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, TenantArchive.ManifestEntry))!.AsObject();
        void SaveManifest(JsonObject manifest) =>
            TenantTransferFixture.ReplaceEntry(zip, TenantArchive.ManifestEntry, manifest.ToJsonString());
        // memberships is the last table loaded: a fault there is found after everything else is in.
        const string lastTable = "tables/memberships.ndjson";

        switch (kind)
        {
            case "extra-entry":
                TenantTransferFixture.ReplaceEntry(zip, "tables/tenant_settings.ndjson", "{}");
                break;
            case "missing-entry":
                zip.GetEntry("tables/sites.ndjson")!.Delete();
                break;
            case "bad-manifest":
                TenantTransferFixture.ReplaceEntry(zip, TenantArchive.ManifestEntry, "{\"formatVersion\": 1}");
                break;
            case "format-version":
                var future = Manifest();
                future["formatVersion"] = 2;
                SaveManifest(future);
                break;
            case "binary-value-no-row-names":
                TenantTransferFixture.ReplaceEntry(zip, $"blobs/assets/{Guid.NewGuid()}", "stray");
                break;
            case "row-without-its-binary-value":
                zip.Entries.First(e => e.FullName.StartsWith("blobs/assets/", StringComparison.Ordinal)).Delete();
                break;
            case "row-with-extra-column":
                var row = JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, lastTable).Trim())!.AsObject();
                row["is_site_admin"] = true;
                TenantTransferFixture.ReplaceEntry(zip, lastTable, row.ToJsonString());
                break;
            case "row-count":
                TenantTransferFixture.ReplaceEntry(zip, lastTable, "");
                break;
            case "unknown-user":
                var member = JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, lastTable).Trim())!.AsObject();
                member["user_id"] = Guid.NewGuid().ToString();
                TenantTransferFixture.ReplaceEntry(zip, lastTable, member.ToJsonString());
                break;
            case "bad-user-list":
                TenantTransferFixture.ReplaceEntry(zip, TenantArchive.UsersEntry, "{\"id\": \"nope\"}");
                break;
            case "duplicate-entry":
                // The right number of entries, one name twice and one name missing.
                zip.GetEntry("tables/sites.ndjson")!.Delete();
                using (var writer = new StreamWriter(zip.CreateEntry(TenantArchive.UsersEntry).Open()))
                    writer.Write("");
                break;
            case "oversized-manifest":
                var padded = Manifest();
                padded["sourceEdition"] = new string('x', 2 * 1024 * 1024);
                SaveManifest(padded);
                break;
            case "row-that-breaks-a-foreign-key":
                var orphan = JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, lastTable).Trim())!.AsObject();
                orphan["site_id"] = Guid.NewGuid().ToString();
                TenantTransferFixture.ReplaceEntry(zip, lastTable, orphan.ToJsonString());
                break;
            case "manifest-with-null-tables":
                var hollow = Manifest();
                hollow["tables"] = null;
                SaveManifest(hollow);
                break;
            case "row-that-is-not-json":
                TenantTransferFixture.ReplaceEntry(zip, lastTable, "not json");
                break;
            case "row-too-large":
                TenantTransferFixture.ReplaceEntry(
                    zip, lastTable, $"{{\"x\":\"{new string('x', TenantArchive.MaxRowChars + 1)}\"}}");
                break;
            case "binary-row-without-id":
                BlankFirstRowId(zip, "tables/assets.ndjson");
                break;
            case "deferred-row-without-id":
                BlankFirstRowId(zip, "tables/list_definitions.ndjson");
                break;
            case "value-of-the-wrong-type":
                var mistyped = JsonNode.Parse(TenantTransferFixture.ReadEntry(zip, lastTable).Trim())!.AsObject();
                mistyped["site_id"] = "not-a-uuid";
                TenantTransferFixture.ReplaceEntry(zip, lastTable, mistyped.ToJsonString());
                break;
        }
    }

    private static void BlankFirstRowId(ZipArchive zip, string entry)
    {
        var lines = TenantTransferFixture.ReadEntry(zip, entry).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var first = JsonNode.Parse(lines[0])!.AsObject();
        first["id"] = null;
        lines[0] = first.ToJsonString();
        TenantTransferFixture.ReplaceEntry(zip, entry, string.Join('\n', lines));
    }

    [Fact]
    public async Task Import_RefusesAnArchiveThatExpandsPastTheBudget()
    {
        await fixture.ResetTargetAsync();
        // A ceiling so low that ten times it is still less than the archive holds.
        var service = fixture.CreateService(maxArchiveBytes: 1024);

        var act = () => ImportAsync(Archive(), service);

        (await act.Should().ThrowAsync<TransferRejectedException>())
            .Which.Message.Should().Contain("expands to more data");
        await AssertTargetUntouchedAsync();
    }

    [Fact]
    public async Task Import_ReportsItsSteps_AndTheLoadAsARisingPercentage()
    {
        await fixture.ResetTargetAsync();
        var reported = new List<Api.Models.TransferProgress>();

        await fixture.CreateService().ImportAsync(
            Archive(), TenantTransferFixture.TargetSlug, actorUserId: null, report: reported.Add);

        reported[0].Should().Be(new Api.Models.TransferProgress("erase"));
        reported[^1].Should().Be(new Api.Models.TransferProgress("finish"));
        var load = reported.Skip(1).SkipLast(1).ToList();
        load.Should().OnlyContain(p => p.Step == "load");
        load.Select(p => p.Percent!.Value).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        load[0].Percent.Should().Be(0);
        load[^1].Percent.Should().Be(100);
    }

    [Fact]
    public async Task Import_WithoutTheSlugAsConfirmation_IsABadRequest()
    {
        await fixture.ResetTargetAsync();
        var reported = new List<Api.Models.TransferProgress>();

        var act = () => fixture.CreateService()
            .ImportAsync(Archive(), TenantTransferFixture.SourceSlug, null, report: reported.Add);

        await act.Should().ThrowAsync<ArgumentException>();
        reported.Should().BeEmpty();
        await AssertTargetUntouchedAsync();
    }

    [Fact]
    public async Task Import_OnAnInstallationWithoutACeiling_IsNotConfigured()
    {
        var service = fixture.CreateService(maxArchiveBytes: null);

        var act = () => ImportAsync(Archive(), service);

        var rejection = (await act.Should().ThrowAsync<TransferRejectedException>()).Which;
        rejection.Code.Should().Be(ApiErrorCodes.TransferNotConfigured);
        rejection.StatusCode.Should().Be(503);
    }

    // ── Rollback ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CancelledWhileLoading_RollsBack()
    {
        await fixture.ResetTargetAsync();
        using var cts = new CancellationTokenSource();
        // The manifest and the user list are read before the erase; the table entries after
        // it. Cancelling on a late read lands in the middle of the load.
        using var archive = new CancelOnReadStream(fixture.Archive, cts, afterReads: 40);

        var act = () => fixture.CreateService().ImportAsync(archive, TenantTransferFixture.TargetSlug, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await AssertTargetUntouchedAsync();
    }

    private sealed class CancelOnReadStream(byte[] content, CancellationTokenSource cts, int afterReads) : MemoryStream(content)
    {
        private int _reads;

        // The one override: a derived MemoryStream routes its span and async reads through it.
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (++_reads == afterReads) cts.Cancel();
            return base.Read(buffer, offset, count);
        }
    }

    // ── Quotas ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_AboveAPlanQuota_RollsBack()
    {
        await fixture.ResetTargetAsync();
        var rollup = new RecordingRollup();
        var service = fixture.CreateService(quotas: new LimitedQuota(QuotaResourceTypes.ProductionSites, limit: 1), rollup: rollup);

        var act = () => ImportAsync(Archive(), service);

        (await act.Should().ThrowAsync<QuotaExceededException>()).Which.ResourceType.Should().Be(QuotaResourceTypes.ProductionSites);
        rollup.Deltas.Should().BeEmpty("nothing was committed, so nothing is reported");
        await AssertTargetUntouchedAsync();
    }

    [Fact]
    public async Task Import_ReportsTheUsageItChanged()
    {
        await fixture.ResetTargetAsync();
        var rollup = new RecordingRollup();

        await ImportAsync(Archive(), fixture.CreateService(rollup: rollup));

        // The target started with one site and nothing else.
        rollup.Deltas[QuotaResourceTypes.ProductionSites].Should().Be(await fixture.CountAsync(source: true, "sites") - 1);
        rollup.Deltas[QuotaResourceTypes.StorageBytes].Should().Be(
            long.Parse((await fixture.QueryAsync(source: true, "SELECT SUM(size_bytes) FROM assets")).Single()));
        rollup.Deltas[QuotaResourceTypes.Spaces].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Import_WhoseCallerLeavesAfterTheCommit_StillReportsUsageAndIsAudited()
    {
        await fixture.ResetTargetAsync();
        using var cts = new CancellationTokenSource();
        // The first report is the first thing after the commit: the caller goes away there.
        var rollup = new RecordingRollup(onRecord: cts.Cancel);

        await fixture.CreateService(rollup: rollup)
            .ImportAsync(Archive(), TenantTransferFixture.TargetSlug, actorUserId: null, cts.Token);

        rollup.Deltas.Keys.Should().BeEquivalentTo(
            [QuotaResourceTypes.ProductionSites, QuotaResourceTypes.Spaces, QuotaResourceTypes.StorageBytes]);
        (await fixture.QueryAsync(source: false, "SELECT action FROM audit_events"))
            .Should().Contain(TenantAuditActions.TransferImported);
    }

    private sealed class LimitedQuota(string key, long limit) : IQuotaEnforcer
    {
        public Task EnsureWithinLimitAsync(string quotaKey, long currentValue, long requestedIncrement, CancellationToken ct = default) =>
            quotaKey == key && currentValue + requestedIncrement > limit
                ? throw new QuotaExceededException(quotaKey, limit)
                : Task.CompletedTask;

        public Task<long> GetLimitAsync(string quotaKey, CancellationToken ct = default) => Task.FromResult(limit);
    }

    private sealed class RecordingRollup(Action? onRecord = null) : IQuotaUsageRollup
    {
        public Dictionary<string, long> Deltas { get; } = [];

        public Task RecordDeltaAsync(string quotaKey, long delta, CancellationToken ct = default)
        {
            onRecord?.Invoke();
            ct.ThrowIfCancellationRequested();
            Deltas[quotaKey] = delta;
            return Task.CompletedTask;
        }
    }

    // ── Concurrency ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Export_DuringAnImportThatHasNotCommitted_StillReadsTheDataAsItWas()
    {
        await fixture.ResetTargetAsync();
        // The quota check is the last step before the commit: hold the import there, with
        // the old data erased and the new data loaded but not yet visible to anyone else.
        var gate = new HoldingQuota();
        var import = ImportAsync(Archive(), fixture.CreateService(quotas: gate));
        await gate.Reached.Task.WaitAsync(TimeSpan.FromMinutes(2));

        using var during = new MemoryStream();
        await fixture.CreateService().ExportAsync(during, null);
        gate.Release.SetResult();
        await import;

        using var zip = new ZipArchive(during, ZipArchiveMode.Read);
        TenantTransferFixture.ReadEntry(zip, "tables/sites.ndjson").Trim().Split('\n')
            .Should().ContainSingle().Which.Should().Contain("\"OLD\"", "readers are not blocked and see one consistent state");
        TenantTransferFixture.ReadEntry(zip, "tables/requests.ndjson").Should().BeEmpty();
    }

    private sealed class HoldingQuota : IQuotaEnforcer
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task EnsureWithinLimitAsync(string quotaKey, long currentValue, long requestedIncrement, CancellationToken ct = default)
        {
            Reached.TrySetResult();
            await Release.Task;
        }

        public Task<long> GetLimitAsync(string quotaKey, CancellationToken ct = default) => Task.FromResult(-1L);
    }

    [Fact]
    public async Task Import_WhileAnotherWriterHoldsTheTables_IsBusy()
    {
        await fixture.ResetTargetAsync();
        await using var other = new NpgsqlConnection(fixture.TargetConnectionString);
        await other.OpenAsync();
        await using var tx = await other.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("LOCK TABLE public.sites IN EXCLUSIVE MODE", other, tx))
            await hold.ExecuteNonQueryAsync();

        var act = () => ImportAsync(Archive());

        var rejection = (await act.Should().ThrowAsync<TransferRejectedException>()).Which;
        rejection.Code.Should().Be(ApiErrorCodes.TransferBusy);
        rejection.StatusCode.Should().Be(409);
        await tx.RollbackAsync();
        await AssertTargetUntouchedAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>The target still is what <see cref="TenantTransferFixture.ResetTargetAsync"/> left.</summary>
    private async Task AssertTargetUntouchedAsync()
    {
        (await fixture.QueryAsync(source: false, "SELECT code FROM sites")).Should().Equal(["OLD"]);
        (await fixture.CountAsync(source: false, "users")).Should().Be(3);
        (await fixture.CountAsync(source: false, "requests")).Should().Be(0);
        (await fixture.CountAsync(source: false, "calendar_feed_tokens")).Should().Be(1);
        (await fixture.QueryAsync(source: false, "SELECT action FROM audit_events")).Should().Equal(["target.kept"]);
    }

    /// <summary>
    /// Every row of a table in a comparable form: encrypted values decrypted, and the values an
    /// import is meant to change — user ids, the owning tenant — brought to what the target
    /// should hold. Sorted, so two dumps of the same data are equal.
    /// </summary>
    private async Task<List<string>> DumpAsync(bool source, TransferTable table)
    {
        var tenantId = source ? TenantTransferFixture.SourceTenantId : TenantTransferFixture.TargetTenantId;
        var rows = await fixture.QueryAsync(source, $"SELECT to_jsonb(t)::text FROM public.\"{table.Name}\" t");
        return rows.Select(row =>
            {
                var values = JsonNode.Parse(row)!.AsObject();
                foreach (var (column, value) in values.ToList())
                {
                    if (value is not JsonValue json || !json.TryGetValue<string>(out var text)) continue;
                    if (fixture.Encryption.IsProtected(text))
                        values[column] = fixture.Encryption.UnprotectString(text, tenantId);
                    else if (column == "data" && text.StartsWith("\\x", StringComparison.Ordinal))
                    {
                        var bytes = Convert.FromHexString(text.AsSpan(2));
                        values[column] = Convert.ToHexString(fixture.Encryption.IsProtected(bytes)
                            ? fixture.Encryption.UnprotectBytes(bytes, tenantId)
                            : bytes);
                    }
                }
                // Setting the deferred column is an UPDATE, and the table's trigger stamps it.
                if (table.DeferredColumn is not null) values.Remove("updated_at");
                // The seed stores floorplans unencrypted, as an old installation would. The
                // import encrypts every value of a column the application encrypts.
                values.Remove("enc_algorithm");
                values.Remove("enc_key_version");
                if (source)
                {
                    foreach (var column in table.UserColumns)
                        if (values[column] is JsonValue user)
                            values[column] = ExpectedUserIds[Guid.Parse(user.GetValue<string>())].ToString();
                    if (values.ContainsKey("tenant_id"))
                        values["tenant_id"] = TenantTransferFixture.TargetTenantId.ToString();
                }
                return values.ToJsonString();
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}

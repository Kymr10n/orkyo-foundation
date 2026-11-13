using System.IO.Compression;
using System.Text;
using Api.Configuration;
using Api.Repositories;
using Api.Security.Encryption;
using Api.Security.Quotas;
using Api.Services;
using Api.Services.Transfer;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orkyo.Foundation.Migrations;
using Orkyo.Foundation.Seed;
using Orkyo.Foundation.Tests.Integration;
using Orkyo.Migrations.Abstractions;

namespace Orkyo.Foundation.Tests.Transfer;

/// <summary>
/// Two migrated tenant databases for the transfer tests: a source that holds a seeded shop
/// (every list, custom field, request tree and floorplan the demo seed produces) and a target
/// that each test resets to a small known state. The source is exported once; the tests
/// import that archive, or a doctored copy of it.
/// </summary>
public sealed class TenantTransferFixture : IAsyncLifetime
{
    private const string SourceDatabase = "transfer_source";
    private const string TargetDatabase = "transfer_target";

    public static readonly Guid SourceTenantId = new("5a000000-0000-0000-0000-000000000001");
    public static readonly Guid TargetTenantId = new("5b000000-0000-0000-0000-000000000002");
    public const string SourceSlug = "source-org";
    public const string TargetSlug = "target-org";

    /// <summary>In both tenants under the same id, but with a different email on the target.</summary>
    public static readonly Guid SameIdUser = new("aaaaaaaa-0000-0000-0000-000000000001");
    /// <summary>In both tenants under the same email, with different ids.</summary>
    public static readonly Guid EmailUserOnSource = new("aaaaaaaa-0000-0000-0000-000000000002");
    public static readonly Guid EmailUserOnTarget = new("bbbbbbbb-0000-0000-0000-000000000002");
    /// <summary>Only in the source: an import makes a placeholder of this user.</summary>
    public static readonly Guid SourceOnlyUser = new("aaaaaaaa-0000-0000-0000-000000000003");
    public const string SourceOnlyEmail = "left@source.test";
    /// <summary>Only in the target: an import must leave this user alone.</summary>
    public static readonly Guid TargetOnlyUser = new("bbbbbbbb-0000-0000-0000-000000000004");

    public const string SecretNote = "Allergic to the coolant in bay 3";
    public const string SecretAddress = "12 Foundry Lane";
    public static readonly byte[] SecretFloorplan = Encoding.UTF8.GetBytes("not really a PNG, but private");

    /// <summary>Generous for every test that is not about the ceiling.</summary>
    public const long DefaultMaxArchiveBytes = 256L * 1024 * 1024;

    private TestPostgresServer _server = null!;

    public IEncryptionService Encryption { get; } = new AesGcmEncryptionService(new byte[32]);
    public string SourceConnectionString => _server.ConnectionStringFor(SourceDatabase);
    public string TargetConnectionString => _server.ConnectionStringFor(TargetDatabase);

    /// <summary>The source tenant's archive, exported once.</summary>
    public byte[] Archive { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _server = await TestPostgresBootstrap.GetAsync();
        var runner = TestPostgresBootstrap.BuildFoundationRunner();
        foreach (var database in new[] { SourceDatabase, TargetDatabase })
        {
            await TestPostgresBootstrap.EnsureDatabaseAsync(_server, database);
            await runner.RunAsync(_server.ConnectionStringFor(database), MigrationTargetDatabase.Tenant,
                $"orkyo:tenant:{database}");
        }

        await SeedSourceAsync();

        using var archive = new MemoryStream();
        await CreateService(source: true).ExportAsync(archive, actorUserId: null);
        Archive = archive.ToArray();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public TenantTransferService CreateService(
        bool source = false,
        IQuotaEnforcer? quotas = null,
        IQuotaUsageRollup? rollup = null,
        long? maxArchiveBytes = DefaultMaxArchiveBytes,
        IEnumerable<IMigrationModule>? modules = null)
    {
        var org = new OrgContext
        {
            OrgId = source ? SourceTenantId : TargetTenantId,
            OrgSlug = source ? SourceSlug : TargetSlug,
            DbConnectionString = source ? SourceConnectionString : TargetConnectionString,
        };
        var connections = new TestDbConnectionFactory(_server.AdminConnectionString, _server.AdminConnectionString);
        return new TenantTransferService(
            new TenantTransferRepository(org, connections),
            new TenantTransferVersionGate(modules ?? [new FoundationMigrationModule()]),
            Encryption,
            org,
            quotas ?? new NoOpQuotaEnforcer(),
            rollup ?? new NoOpQuotaUsageRollup(),
            new TenantUserService(connections, NullLogger<TenantUserService>.Instance),
            Deployment(maxArchiveBytes),
            TimeProvider.System);
    }

    private static DeploymentConfig Deployment(long? maxArchiveBytes) => new()
    {
        PublicUrl = "http://app",
        AuthPublicUrl = "http://kc",
        AppBaseUrl = "http://app",
        CorsAllowedOrigins = "",
        OidcAuthority = "http://kc/realms/orkyo",
        KeycloakUrl = "http://kc",
        KeycloakRealm = "orkyo",
        KeycloakBackendClientId = "id",
        KeycloakBackendClientSecret = "s",
        KeycloakPasswordCheckClientId = TestConstants.CheckClientId,
        KeycloakPasswordCheckClientSecret = TestConstants.CheckClientCredential,
        PostgresConnectionString = "cs",
        MasterEncryptionKey = TestConstants.MasterEncryptionKey,
        TransferMaxArchiveBytes = maxArchiveBytes,
        Version = "9.9.9-test",
        DeploymentMode = "self-hosted",
    };

    // ── Source ───────────────────────────────────────────────────────────────

    private async Task SeedSourceAsync()
    {
        await using var conn = new NpgsqlConnection(SourceConnectionString);
        await conn.OpenAsync();
        // The migrations just created the citext type; Npgsql cached this database's type
        // list when the runner first connected, before the type existed.
        await conn.ReloadTypesAsync();
        await TestResourceTypes.EnsureAsync(conn);
        await SeedRunner.RunAsync(conn, new SeedOptions
        {
            Profile = "manufacturing",
            Scale = "tiny",
            TenantId = SourceTenantId,
            ReferenceDate = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
        });

        // What the seed does not produce: users the data points at, values encrypted at rest
        // under the source tenant, and rows in the tables that must not travel.
        await ExecuteAsync(conn, @"
            INSERT INTO users (id, email, display_name) VALUES
                (@same, 'same@source.test', 'Same Id'),
                (@email, 'match@both.test', 'Email Match'),
                (@only, @onlyEmail, NULL);

            UPDATE resources SET linked_user_id = @same
            WHERE id = (SELECT r.id FROM resources r JOIN resource_types t ON t.id = r.resource_type_id
                        WHERE t.key = 'person' ORDER BY r.id LIMIT 1);
            UPDATE resources SET notes = @note
            WHERE id = (SELECT id FROM resources ORDER BY id LIMIT 1);
            UPDATE sites SET address = @address
            WHERE id = (SELECT id FROM sites ORDER BY id LIMIT 1);
            UPDATE assets SET data = @floorplan, size_bytes = @floorplanSize,
                              enc_algorithm = @algorithm, enc_key_version = @keyVersion,
                              created_by_user_id = @email, updated_by_user_id = @only
            WHERE id = (SELECT id FROM assets ORDER BY id LIMIT 1);

            INSERT INTO resource_scan_codes (resource_id, code, created_by_user_id)
            SELECT id, 'QR-0001', @only FROM resources ORDER BY id LIMIT 1;
            INSERT INTO memberships (user_id, site_id, role)
            SELECT @email, id, 'writer' FROM sites ORDER BY id LIMIT 1;

            INSERT INTO calendar_feed_tokens (user_id, token_hash) VALUES (@same, repeat('a', 64));
            INSERT INTO tenant_settings (key, value, category) VALUES ('branding.product_name', 'Source Shop', 'branding');
            INSERT INTO audit_events (action) VALUES ('source.only');",
            ("same", SameIdUser), ("email", EmailUserOnSource), ("only", SourceOnlyUser), ("onlyEmail", SourceOnlyEmail),
            ("note", Encryption.ProtectString(SecretNote, SourceTenantId)!),
            ("address", Encryption.ProtectString(SecretAddress, SourceTenantId)!),
            ("floorplan", Encryption.ProtectBytes(SecretFloorplan, SourceTenantId)),
            ("floorplanSize", (long)SecretFloorplan.Length),
            ("algorithm", Encryption.Algorithm), ("keyVersion", Encryption.KeyVersion));
    }

    // ── Target ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts the target back to its starting point: three users, one row in each kind of table
    /// an import must leave alone, and travelling data an import must replace.
    /// </summary>
    public async Task ResetTargetAsync()
    {
        await using var conn = new NpgsqlConnection(TargetConnectionString);
        await conn.OpenAsync();

        var tables = TenantTransferTables.All
            .Where(t => t.Name != "orkyo_schema_migrations")
            .Select(t => $"public.\"{t.Name}\"");
        await ExecuteAsync(conn, $"TRUNCATE TABLE {string.Join(", ", tables)} CASCADE");

        await ExecuteAsync(conn, @"
            INSERT INTO users (id, email, display_name) VALUES
                (@same, 'renamed@target.test', 'Same Id, Renamed'),
                (@email, 'MATCH@both.test', 'Email Match On Target'),
                (@only, 'only@target.test', 'Target Only');

            INSERT INTO sites (id, code, name) VALUES ('7a000000-0000-0000-0000-000000000001', 'OLD', 'Goes Away');
            INSERT INTO calendar_feed_tokens (user_id, token_hash) VALUES (@only, repeat('b', 64));

            INSERT INTO audit_events (action) VALUES ('target.kept');
            INSERT INTO user_preferences (user_id, preferences) VALUES (@only, '{""theme"":""dark""}');
            INSERT INTO tenant_settings (key, value, category) VALUES ('branding.product_name', 'Target Shop', 'branding');
            INSERT INTO ai_conversations (id, user_id, title, entries, transcript) VALUES (gen_random_uuid(), @only, 'Kept chat', '[]', '[]');",
            ("same", SameIdUser), ("email", EmailUserOnTarget), ("only", TargetOnlyUser));
    }

    // ── Archive doctoring ────────────────────────────────────────────────────

    /// <summary>A copy of the exported archive with <paramref name="change"/> applied to it.</summary>
    public MemoryStream DoctoredArchive(Action<ZipArchive> change)
    {
        var copy = new MemoryStream();
        copy.Write(Archive);
        using (var zip = new ZipArchive(copy, ZipArchiveMode.Update, leaveOpen: true))
            change(zip);
        copy.Position = 0;
        return copy;
    }

    public static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }

    public static void ReplaceEntry(ZipArchive zip, string name, string content)
    {
        zip.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        writer.Write(content);
    }

    // ── SQL helpers ──────────────────────────────────────────────────────────

    public async Task<List<string>> QueryAsync(bool source, string sql)
    {
        await using var conn = new NpgsqlConnection(source ? SourceConnectionString : TargetConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.IsDBNull(0) ? "" : reader.GetValue(0).ToString()!);
        return rows;
    }

    public async Task<long> CountAsync(bool source, string table) =>
        long.Parse((await QueryAsync(source, $"SELECT COUNT(*) FROM public.\"{table}\"")).Single());

    private static async Task ExecuteAsync(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}

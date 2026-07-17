using System.Reflection;
using System.Security.Cryptography;
using Api.Models;
using Api.Models.Preset;
using Npgsql;

namespace Api.Services;

/// <summary>
/// Applies starter templates to newly created tenant databases.
/// Templates bootstrap the DB with preset data (criteria, groups, templates)
/// and optionally with full demo data (sites, spaces, requests, floorplan).
/// </summary>
public interface IStarterTemplateService
{
    /// <summary>
    /// Apply a named starter template to a freshly provisioned tenant database.
    /// </summary>
    /// <param name="tenantId">Control-plane tenant ID.</param>
    /// <param name="dbIdentifier">Tenant database name (e.g. "tenant_acme").</param>
    /// <param name="userId">Owning user ID (used for audit trail).</param>
    /// <param name="templateKey">One of: "demo", "camping-site", "construction-site", "manufacturing".</param>
    Task ApplyStarterTemplateAsync(Guid tenantId, string dbIdentifier, Guid userId, string templateKey, CancellationToken ct = default);

    /// <summary>
    /// Returns metadata for all available starter templates.
    /// </summary>
    IReadOnlyList<StarterTemplateInfo> GetAvailableTemplates();
}

public class StarterTemplateService : IStarterTemplateService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<StarterTemplateService> _logger;



    public StarterTemplateService(
        IDbConnectionFactory connectionFactory,
        ILogger<StarterTemplateService> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public IReadOnlyList<StarterTemplateInfo> GetAvailableTemplates() => StarterTemplateCatalog.All;

    public async Task ApplyStarterTemplateAsync(
        Guid tenantId, string dbIdentifier, Guid userId, string templateKey, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Applying starter template {Template} to tenant {TenantId} (db={Db})",
            templateKey, tenantId, dbIdentifier);

        if (StarterTemplateCatalog.IsDemoTemplate(templateKey))
        {
            await ApplyDemoTemplateAsync(tenantId, dbIdentifier, ct);
            return;
        }

        if (StarterTemplateCatalog.IsPresetTemplate(templateKey))
        {
            await ApplyPresetTemplateAsync(dbIdentifier, templateKey, ct);
            return;
        }

        _logger.LogWarning("Unknown starter template: {Template}", templateKey);
        throw new ArgumentException($"Unknown starter template: {templateKey}");
    }

    // -- Preset application (reuses PresetApplier) --------------------

    private async Task ApplyPresetTemplateAsync(string dbIdentifier, string templateKey, CancellationToken ct = default)
    {
        var preset = PresetTemplateLoader.LoadPreset(
            templateKey,
            Path.Combine(AppContext.BaseDirectory, "Presets"),
            Assembly.GetExecutingAssembly());

        await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await PresetApplier.ApplyAsync(conn, tx, preset);
            await tx.CommitAsync(ct);
            _logger.LogInformation("Applied preset {PresetId} to tenant database {Db}", preset.PresetId, dbIdentifier);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    // -- Demo template ------------------------------------------------

    private async Task ApplyDemoTemplateAsync(Guid tenantId, string dbIdentifier, CancellationToken ct = default)
    {
        var site1Id = Guid.NewGuid();
        var site2Id = Guid.NewGuid();

        await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            var sql = LoadSqlFile("demo/demo-seed.sql");
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("site1Id", site1Id);
            cmd.Parameters.AddWithValue("site2Id", site2Id);
            await cmd.ExecuteNonQueryAsync(ct);

            await tx.CommitAsync(ct);
            _logger.LogInformation("Applied demo seed data to tenant {TenantId}", tenantId);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Copy demo floorplan (best-effort, outside transaction)
        await CopyDemoFloorplanAsync(dbIdentifier, tenantId, site1Id, ct);
    }

    // -- Floorplan ----------------------------------------------------

    private async Task CopyDemoFloorplanAsync(string dbIdentifier, Guid tenantId, Guid siteId, CancellationToken ct = default)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("demo-floorplan.png"));

            if (resourceName == null)
            {
                _logger.LogWarning("Demo floorplan embedded resource not found - skipping");
                return;
            }

            await using var resourceStream = assembly.GetManifestResourceStream(resourceName)!;

            // Use MemoryStream so we can read the stream twice (save + dimensions)
            using var ms = new MemoryStream();
            await resourceStream.CopyToAsync(ms, ct);

            var data = ms.ToArray();
            if (!ImageHeaderReader.TryGetDimensions(data, out var width, out var height))
            {
                _logger.LogWarning("Demo floorplan dimensions could not be read - skipping");
                return;
            }

            var checksum = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

            await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
            await conn.OpenAsync(ct);
            await using var cmd = new Npgsql.NpgsqlCommand(@"
                INSERT INTO assets (
                    tenant_id, owner_type, owner_id, asset_type, file_name, content_type,
                    size_bytes, checksum_sha256, width_px, height_px, storage_kind, data,
                    external_uri, created_by_user_id, updated_by_user_id
                )
                VALUES (
                    @tenantId, @ownerType, @siteId, @assetType, @fileName, @mime,
                    @size, @checksum, @w, @h, @storageKind, @data,
                    NULL, NULL, NULL
                )
                ON CONFLICT (tenant_id, owner_type, owner_id, asset_type)
                WHERE asset_type = 'floorplan'
                DO UPDATE SET
                    file_name = EXCLUDED.file_name,
                    content_type = EXCLUDED.content_type,
                    size_bytes = EXCLUDED.size_bytes,
                    checksum_sha256 = EXCLUDED.checksum_sha256,
                    width_px = EXCLUDED.width_px,
                    height_px = EXCLUDED.height_px,
                    storage_kind = EXCLUDED.storage_kind,
                    data = EXCLUDED.data,
                    external_uri = NULL,
                    updated_at = NOW()", conn);
            cmd.Parameters.AddWithValue("tenantId", tenantId);
            cmd.Parameters.AddWithValue("ownerType", AssetOwnerTypes.Site);
            cmd.Parameters.AddWithValue("siteId", siteId);
            cmd.Parameters.AddWithValue("assetType", AssetTypes.Floorplan);
            cmd.Parameters.AddWithValue("fileName", "demo-floorplan.png");
            cmd.Parameters.AddWithValue("mime", "image/png");
            cmd.Parameters.AddWithValue("size", (long)data.Length);
            cmd.Parameters.AddWithValue("checksum", checksum);
            cmd.Parameters.AddWithValue("w", width);
            cmd.Parameters.AddWithValue("h", height);
            cmd.Parameters.AddWithValue("storageKind", AssetStorageKinds.Postgres);
            cmd.Parameters.AddWithValue("data", data);
            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("Installed demo floorplan for site {SiteId}", siteId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to install demo floorplan - continuing without it");
        }
    }

    // -- File loaders -------------------------------------------------


    private static string LoadSqlFile(string relativePath)
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Presets", relativePath);

        if (File.Exists(filePath))
            return File.ReadAllText(filePath);

        throw new FileNotFoundException($"Demo seed SQL not found: {filePath}");
    }
}

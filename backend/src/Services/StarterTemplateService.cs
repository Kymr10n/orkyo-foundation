using System.Reflection;
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
    Task ApplyStarterTemplateAsync(Guid tenantId, string dbIdentifier, Guid userId, string templateKey);

    /// <summary>
    /// Returns metadata for all available starter templates.
    /// </summary>
    IReadOnlyList<StarterTemplateInfo> GetAvailableTemplates();
}

public class StarterTemplateService : IStarterTemplateService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<StarterTemplateService> _logger;



    public StarterTemplateService(
        IDbConnectionFactory connectionFactory,
        IFileStorageService fileStorageService,
        ILogger<StarterTemplateService> logger)
    {
        _connectionFactory = connectionFactory;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public IReadOnlyList<StarterTemplateInfo> GetAvailableTemplates() => StarterTemplateCatalog.All;

    public async Task ApplyStarterTemplateAsync(
        Guid tenantId, string dbIdentifier, Guid userId, string templateKey)
    {
        _logger.LogInformation(
            "Applying starter template {Template} to tenant {TenantId} (db={Db})",
            templateKey, tenantId, dbIdentifier);

        if (StarterTemplateCatalog.IsDemoTemplate(templateKey))
        {
            await ApplyDemoTemplateAsync(tenantId, dbIdentifier);
            return;
        }

        if (StarterTemplateCatalog.IsPresetTemplate(templateKey))
        {
            await ApplyPresetTemplateAsync(dbIdentifier, templateKey);
            return;
        }

        _logger.LogWarning("Unknown starter template: {Template}", templateKey);
        throw new ArgumentException($"Unknown starter template: {templateKey}");
    }

    // -- Preset application (reuses PresetApplier) --------------------

    private async Task ApplyPresetTemplateAsync(string dbIdentifier, string templateKey)
    {
        var preset = PresetTemplateLoader.LoadPreset(
            templateKey,
            Path.Combine(AppContext.BaseDirectory, "Presets"),
            Assembly.GetExecutingAssembly());

        await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            await PresetApplier.ApplyAsync(conn, tx, preset);
            await tx.CommitAsync();
            _logger.LogInformation("Applied preset {PresetId} to tenant database {Db}", preset.PresetId, dbIdentifier);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // -- Demo template ------------------------------------------------

    private async Task ApplyDemoTemplateAsync(Guid tenantId, string dbIdentifier)
    {
        var site1Id = Guid.NewGuid();
        var site2Id = Guid.NewGuid();

        await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var sql = LoadSqlFile("demo/demo-seed.sql");
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("site1Id", site1Id);
            cmd.Parameters.AddWithValue("site2Id", site2Id);
            await cmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            _logger.LogInformation("Applied demo seed data to tenant {TenantId}", tenantId);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        // Copy demo floorplan (best-effort, outside transaction)
        await CopyDemoFloorplanAsync(dbIdentifier, tenantId, site1Id);
    }

    // -- Floorplan ----------------------------------------------------

    private async Task CopyDemoFloorplanAsync(string dbIdentifier, Guid tenantId, Guid siteId)
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
            await resourceStream.CopyToAsync(ms);

            // Save via IFileStorageService
            ms.Position = 0;
            var relativePath = await _fileStorageService.SaveFloorplanFromStreamAsync(
                ms, "image/png", siteId, tenantId);

            // Get dimensions
            ms.Position = 0;
            var (width, height) = await _fileStorageService.GetImageDimensionsAsync(ms);

            // Update site record with floorplan metadata
            await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
            await conn.OpenAsync();
            await using var cmd = SiteFloorplanCommandFactory.CreateUpdateFloorplanCommand(
                conn,
                siteId: siteId,
                imagePath: relativePath,
                mimeType: "image/png",
                fileSizeBytes: ms.Length,
                widthPx: width,
                heightPx: height);
            await cmd.ExecuteNonQueryAsync();

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

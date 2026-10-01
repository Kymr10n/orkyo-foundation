using System.Reflection;
using Api.Models;
using Api.Models.Preset;
using Api.Validators;
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
    /// Apply a named starter template to a freshly provisioned tenant database. A preset-backed
    /// template bootstraps a database once: when <c>preset_applications</c> already records its
    /// preset, the call logs and returns without touching anything, so a host can run it at every
    /// start. Re-applying is the admin UI's job (Settings → Presets).
    /// </summary>
    /// <param name="tenantId">Control-plane tenant ID. Used for logging only.</param>
    /// <param name="dbIdentifier">Tenant database name (e.g. "tenant_acme"); a single-tenant factory ignores it.</param>
    /// <param name="userId">Owning user ID. The foundation implementation does not read it.</param>
    /// <param name="templateKey">One of: "demo", "camping-site", "construction-site", "manufacturing", "office".</param>
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

        // A shipped file that fails its own validator is a packaging bug and must say so, not
        // fail three tables later on a foreign key with a constraint name for a message.
        var validation = await new PresetValidator().ValidateAsync(preset, ct);
        if (!validation.IsValid)
            throw new InvalidOperationException(
                $"Shipped preset '{preset.PresetId}' is invalid: "
                + string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

        await using var conn = _connectionFactory.CreateConnectionForDatabase(dbIdentifier);
        await conn.OpenAsync(ct);

        // Once only. A starter template bootstraps an empty database; a host may call this at
        // every start, and a second run must not rewrite descriptions, colours, template items
        // and allocation modes the tenant has since edited.
        await using (var check = new NpgsqlCommand(
                         "SELECT 1 FROM preset_applications WHERE preset_id = @presetId", conn))
        {
            check.Parameters.AddWithValue("presetId", preset.PresetId);
            if (await check.ExecuteScalarAsync(ct) is not null)
            {
                _logger.LogInformation(
                    "Starter template {Template} already applied (preset {PresetId}) to {Db}; skipping",
                    templateKey, preset.PresetId, dbIdentifier);
                return;
            }
        }

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

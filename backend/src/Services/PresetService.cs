using Api.Helpers;
using Api.Models;
using Api.Models.Preset;
using Api.Repositories;
using Npgsql;

using static Api.Helpers.KeyHelpers;

namespace Api.Services;

public class PresetService : IPresetService
{
    private readonly OrgContext _orgContext;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICriteriaRepository _criteriaRepo;
    private readonly ISpaceGroupRepository _spaceGroupRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly ILogger<PresetService> _logger;

    public PresetService(
        OrgContext orgContext,
        IDbConnectionFactory connectionFactory,
        ICriteriaRepository criteriaRepo,
        ISpaceGroupRepository spaceGroupRepo,
        ITemplateRepository templateRepo,
        ILogger<PresetService> logger)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
        _criteriaRepo = criteriaRepo;
        _spaceGroupRepo = spaceGroupRepo;
        _templateRepo = templateRepo;
        _logger = logger;
    }

    public async Task<PresetValidationResult> ValidateAsync(Preset preset)
        => await Task.FromResult(PresetValidator.Validate(preset));

    public async Task<PresetApplicationResult> ApplyAsync(Preset preset, Guid userId)
    {
        var validation = await ValidateAsync(preset);
        if (!validation.IsValid)
            return new PresetApplicationResult { Success = false, Error = string.Join("; ", validation.Errors) };

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var stats = await PresetApplier.ApplyAsync(conn, transaction, preset, userId);
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Applied preset {PresetId} v{Version}: {CriteriaCreated} criteria created, {CriteriaUpdated} updated, " +
                "{GroupsCreated} groups created, {GroupsUpdated} updated, {TemplatesCreated} templates created, {TemplatesUpdated} updated",
                preset.PresetId, preset.Version,
                stats.CriteriaCreated, stats.CriteriaUpdated,
                stats.SpaceGroupsCreated, stats.SpaceGroupsUpdated,
                stats.TemplatesCreated, stats.TemplatesUpdated);

            return new PresetApplicationResult { Success = true, Stats = stats };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Failed to apply preset {PresetId}", preset.PresetId);
            return new PresetApplicationResult { Success = false, Error = $"Failed to apply preset: {ex.Message}" };
        }
    }

    public async Task<Preset> ExportAsync(string presetId, string name, string? description = null)
    {
        var criteria = await _criteriaRepo.GetAllAsync();
        var presetCriteria = criteria.Select(c => new PresetCriterion
        {
            Key = GenerateKey(c.Name),
            Name = c.Name,
            Description = c.Description,
            DataType = c.DataType,
            EnumValues = c.EnumValues,
            Unit = c.Unit
        }).ToList();

        var criterionKeyMap = presetCriteria.ToDictionary(c => c.Name, c => c.Key);
        var criterionIdToKey = criteria.ToDictionary(c => c.Id, c => criterionKeyMap[c.Name]);

        var groups = await _spaceGroupRepo.GetAllAsync();
        var presetGroups = groups.Select(g => new PresetSpaceGroup
        {
            Key = GenerateKey(g.Name),
            Name = g.Name,
            Description = g.Description,
            Color = g.Color,
            DisplayOrder = g.DisplayOrder
        }).ToList();

        var presetTemplates = new PresetTemplates
        {
            Space = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync("space"), criterionIdToKey),
            Group = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync("group"), criterionIdToKey),
            Request = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync("request"), criterionIdToKey)
        };

        return new Preset
        {
            PresetId = presetId,
            Name = name,
            Description = description,
            Version = "1.0.0",
            CreatedAt = DateTime.UtcNow,
            Contents = new PresetContents
            {
                Criteria = presetCriteria,
                SpaceGroups = presetGroups,
                Templates = presetTemplates
            }
        };
    }

    public async Task<List<PresetApplication>> GetApplicationsAsync()
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        var applications = new List<PresetApplication>();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, preset_id, preset_version, applied_at, updated_at, applied_by_user_id
            FROM preset_applications
            ORDER BY applied_at DESC", conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            applications.Add(new PresetApplication
            {
                Id = reader.GetGuid(0),
                PresetId = reader.GetString(1),
                PresetVersion = reader.GetString(2),
                AppliedAt = reader.GetDateTime(3),
                UpdatedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                AppliedByUserId = reader.IsDBNull(5) ? null : reader.GetGuid(5)
            });
        }

        return applications;
    }

    private async Task<List<PresetTemplate>> ConvertTemplatesAsync(List<Template> templates, Dictionary<Guid, string> criterionIdToKey)
    {
        var result = new List<PresetTemplate>();
        foreach (var template in templates)
        {
            var items = await _templateRepo.GetTemplateItemsAsync(template.Id);
            result.Add(new PresetTemplate
            {
                Key = GenerateKey(template.Name),
                Name = template.Name,
                Description = template.Description,
                DurationValue = template.DurationValue,
                DurationUnit = template.DurationUnit,
                FixedStart = template.FixedStart,
                FixedEnd = template.FixedEnd,
                FixedDuration = template.FixedDuration,
                Items = items
                    .Where(i => criterionIdToKey.ContainsKey(i.CriterionId))
                    .Select(i => new PresetTemplateItem { CriterionKey = criterionIdToKey[i.CriterionId], Value = i.Value })
                    .ToList()
            });
        }
        return result;
    }
}

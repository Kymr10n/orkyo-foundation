using Api.Constants;
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
    private readonly IResourceGroupRepository _resourceGroupRepo;
    private readonly IResourceTypeRepository _resourceTypeRepo;
    private readonly ITemplateRepository _templateRepo;
    private readonly ILogger<PresetService> _logger;
    private readonly TimeProvider _time;
    private readonly FluentValidation.IValidator<Preset> _validator;

    public PresetService(
        OrgContext orgContext,
        IDbConnectionFactory connectionFactory,
        ICriteriaRepository criteriaRepo,
        IResourceGroupRepository resourceGroupRepo,
        IResourceTypeRepository resourceTypeRepo,
        ITemplateRepository templateRepo,
        ILogger<PresetService> logger,
        TimeProvider time,
        FluentValidation.IValidator<Preset> validator)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
        _criteriaRepo = criteriaRepo;
        _resourceGroupRepo = resourceGroupRepo;
        _resourceTypeRepo = resourceTypeRepo;
        _templateRepo = templateRepo;
        _logger = logger;
        _time = time;
        _validator = validator;
    }

    public async Task<PresetValidationResult> ValidateAsync(Preset preset, CancellationToken ct = default)
    {
        var result = await _validator.ValidateAsync(preset, ct);
        return new PresetValidationResult(result.IsValid, result.Errors.Select(e => e.ErrorMessage).ToList());
    }

    public async Task<PresetApplicationResult> ApplyAsync(Preset preset, Guid userId, CancellationToken ct = default)
    {
        var validation = await ValidateAsync(preset, ct);
        if (!validation.IsValid)
            return new PresetApplicationResult { Success = false, Error = string.Join("; ", validation.Errors) };

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);
        // A failure rolls back on dispose and reaches AppExceptionHandler, which answers with a
        // code and without the exception text: the message used to carry Npgsql's table and
        // constraint names to the client.
        await using var transaction = await conn.BeginTransactionAsync(ct);

        var stats = await PresetApplier.ApplyAsync(conn, transaction, preset, userId);
        await transaction.CommitAsync(ct);

        _logger.LogInformation(
            "Applied preset {PresetId} v{Version}: {CriteriaCreated} criteria created, {CriteriaUpdated} updated, " +
            "{GroupsCreated} groups created, {GroupsUpdated} updated, {TemplatesCreated} templates created, {TemplatesUpdated} updated",
            preset.PresetId, preset.Version,
            stats.CriteriaCreated, stats.CriteriaUpdated,
            stats.SpaceGroupsCreated, stats.SpaceGroupsUpdated,
            stats.TemplatesCreated, stats.TemplatesUpdated);

        return new PresetApplicationResult { Success = true, Stats = stats };
    }

    public async Task<Preset> ExportAsync(string presetId, string name, string? description = null, CancellationToken ct = default)
    {
        var criteria = await _criteriaRepo.GetAllAsync(ct);
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

        // Export follows the same rule as apply: groups of every placeable type, since that is
        // what the floorplan the preset captures actually holds.
        var groups = await _resourceGroupRepo.GetByTypeKeysAsync(
            await _resourceTypeRepo.GetPlaceableKeysAsync(ct), ct);
        var presetGroups = groups.Select(g => new PresetSpaceGroup
        {
            Key = GenerateKey(g.Name),
            Name = g.Name,
            Description = g.Description,
            Color = g.Color,
            DisplayOrder = g.DisplayOrder ?? 0
        }).ToList();

        var presetTemplates = new PresetTemplates
        {
            Space = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync(TemplateEntityTypes.Space, ct), criterionIdToKey, ct),
            Group = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync(TemplateEntityTypes.Group, ct), criterionIdToKey, ct),
            Request = await ConvertTemplatesAsync(await _templateRepo.GetAllAsync(TemplateEntityTypes.Request, ct), criterionIdToKey, ct)
        };

        return new Preset
        {
            PresetId = presetId,
            Name = name,
            Description = description,
            Version = "1.0.0",
            CreatedAt = _time.GetUtcNow().UtcDateTime,
            Contents = new PresetContents
            {
                Criteria = presetCriteria,
                SpaceGroups = presetGroups,
                Templates = presetTemplates
            }
        };
    }

    public async Task<List<PresetApplication>> GetApplicationsAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync(ct);

        var applications = new List<PresetApplication>();
        await using var cmd = new NpgsqlCommand(@"
            SELECT id, preset_id, preset_version, applied_at, updated_at, applied_by_user_id
            FROM preset_applications
            ORDER BY applied_at DESC", conn);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            applications.Add(new PresetApplication
            {
                Id = reader.GetGuid("id"),
                PresetId = reader.GetString("preset_id"),
                PresetVersion = reader.GetString("preset_version"),
                AppliedAt = reader.GetDateTime("applied_at"),
                UpdatedAt = reader.GetNullableDateTime("updated_at"),
                AppliedByUserId = reader.GetNullableGuid("applied_by_user_id")
            });
        }

        return applications;
    }

    private Task<List<PresetTemplate>> ConvertTemplatesAsync(
        List<Template> templates, Dictionary<Guid, string> criterionIdToKey, CancellationToken ct) =>
        TemplateProjection.ProjectAsync(_templateRepo, templates, criterionIdToKey, (template, items) => new PresetTemplate
        {
            Key = GenerateKey(template.Name),
            Name = template.Name,
            Description = template.Description,
            DurationValue = template.DurationValue,
            DurationUnit = template.DurationUnit,
            FixedStart = template.FixedStart,
            FixedEnd = template.FixedEnd,
            FixedDuration = template.FixedDuration,
            Items = items.Select(i => new PresetTemplateItem { CriterionKey = i.CriterionKey, Value = i.Value }).ToList()
        }, ct);
}

using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface ITemplateRepository
{
    Task<List<Template>> GetAllAsync(string entityType, CancellationToken ct = default);
    Task<Template?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Template> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default);
    Task<Template?> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<List<TemplateItem>> GetTemplateItemsAsync(Guid templateId, CancellationToken ct = default);
    Task<TemplateItem> CreateTemplateItemAsync(TemplateItem item, CancellationToken ct = default);
    Task<bool> DeleteTemplateItemAsync(Guid id, CancellationToken ct = default);
}

public class TemplateRepository : ITemplateRepository
{
    private static readonly HashSet<string> ValidEntityTypes = new(StringComparer.Ordinal)
        { "request", "space", "group" };

    private readonly OrgContext _orgContext;
    private readonly IOrgDbConnectionFactory _connectionFactory;

    public TemplateRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    {
        _orgContext = orgContext;
        _connectionFactory = connectionFactory;
    }

    private const string TemplateCols = @"
        id, name, description, entity_type,
        duration_value, duration_unit,
        fixed_start, fixed_end, fixed_duration,
        created_at, updated_at";

    private static Template MapTemplate(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(0),
        Name = r.GetString(1),
        Description = r.IsDBNull(2) ? null : r.GetString(2),
        EntityType = r.GetString(3),
        DurationValue = r.IsDBNull(4) ? null : r.GetInt32(4),
        DurationUnit = r.IsDBNull(5) ? null : r.GetString(5),
        FixedStart = r.GetBoolean(6),
        FixedEnd = r.GetBoolean(7),
        FixedDuration = r.GetBoolean(8),
        CreatedAt = r.GetDateTime(9),
        UpdatedAt = r.GetDateTime(10)
    };

    public async Task<List<Template>> GetAllAsync(string entityType, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryListAsync(
            $"SELECT {TemplateCols} FROM templates WHERE entity_type = @EntityType ORDER BY name LIMIT 500",
            p => p.AddWithValue("EntityType", entityType), MapTemplate, ct);
    }

    public async Task<Template?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QuerySingleOrDefaultAsync(
            $"SELECT {TemplateCols} FROM templates WHERE id = @Id",
            p => p.AddWithValue("Id", id), MapTemplate, ct);
    }

    public async Task<Template> CreateAsync(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (!ValidEntityTypes.Contains(request.EntityType))
            throw new ArgumentException($"Invalid entity type: {request.EntityType}");
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Name is required");
        if (request.Name.Length > 255)
            throw new ArgumentException("Name must be 255 characters or fewer");
        if (request.Description?.Length > 255)
            throw new ArgumentException("Description must be 255 characters or fewer");

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return (await conn.QuerySingleOrDefaultAsync($@"
            INSERT INTO templates (name, description, entity_type, duration_value, duration_unit, fixed_start, fixed_end, fixed_duration)
            VALUES (@Name, @Description, @EntityType, @DurationValue, @DurationUnit, @FixedStart, @FixedEnd, @FixedDuration)
            RETURNING {TemplateCols}",
            p =>
            {
                p.AddWithValue("Name", request.Name);
                p.AddNullable("Description", request.Description);
                p.AddWithValue("EntityType", request.EntityType);
                p.AddNullable("DurationValue", request.DurationValue);
                p.AddNullable("DurationUnit", request.DurationUnit);
                p.AddWithValue("FixedStart", request.FixedStart);
                p.AddWithValue("FixedEnd", request.FixedEnd);
                p.AddWithValue("FixedDuration", request.FixedDuration);
            }, MapTemplate, ct))!;
    }

    public async Task<Template?> UpdateAsync(Guid id, UpdateTemplateRequest request, CancellationToken ct = default)
    {
        if (!ValidEntityTypes.Contains(request.EntityType))
            throw new ArgumentException($"Invalid entity type: {request.EntityType}");

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        return await conn.QuerySingleOrDefaultAsync($@"
            UPDATE templates SET
                name = @Name, description = @Description, entity_type = @EntityType,
                duration_value = @DurationValue, duration_unit = @DurationUnit,
                fixed_start = @FixedStart, fixed_end = @FixedEnd, fixed_duration = @FixedDuration,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING {TemplateCols}",
            p =>
            {
                p.AddWithValue("Id", id);
                p.AddWithValue("Name", request.Name);
                p.AddNullable("Description", request.Description);
                p.AddWithValue("EntityType", request.EntityType);
                p.AddNullable("DurationValue", request.DurationValue);
                p.AddNullable("DurationUnit", request.DurationUnit);
                p.AddWithValue("FixedStart", request.FixedStart);
                p.AddWithValue("FixedEnd", request.FixedEnd);
                p.AddWithValue("FixedDuration", request.FixedDuration);
            }, MapTemplate, ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM templates WHERE id = @Id",
            p => p.AddWithValue("Id", id), ct) > 0;
    }

    public async Task<List<TemplateItem>> GetTemplateItemsAsync(Guid templateId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.QueryListAsync(@"
            SELECT ti.id, ti.template_id, ti.criterion_id, ti.value,
                   ti.created_at, ti.updated_at,
                   c.name AS criterion_name, c.data_type AS criterion_data_type
            FROM template_items ti
            INNER JOIN criteria c ON ti.criterion_id = c.id
            WHERE ti.template_id = @TemplateId
            ORDER BY c.name",
            p => p.AddWithValue("TemplateId", templateId),
            r => new TemplateItem
            {
                Id = r.GetGuid(0),
                TemplateId = r.GetGuid(1),
                CriterionId = r.GetGuid(2),
                Value = r.GetString(3),
                CreatedAt = r.GetDateTime(4),
                UpdatedAt = r.GetDateTime(5),
                CriterionName = r.GetString(6),
                CriterionDataType = r.GetString(7),
                CriterionCategory = null
            }, ct);
    }

    public async Task<TemplateItem> CreateTemplateItemAsync(TemplateItem item, CancellationToken ct = default)
    {
        var template = await GetByIdAsync(item.TemplateId, ct);
        if (template is null)
            throw new ArgumentException($"Template not found: {item.TemplateId}");
        if (string.IsNullOrEmpty(item.Value))
            throw new ArgumentException("Value is required");
        try { JsonDocument.Parse(item.Value); }
        catch (JsonException) { throw new ArgumentException("Value must be valid JSON"); }

        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);

        var criterionExists = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM criteria WHERE id = @CriterionId)",
            p => p.AddWithValue("CriterionId", item.CriterionId), ct);
        if (!criterionExists)
            throw new ArgumentException($"Criterion not found: {item.CriterionId}");

        try
        {
            return (await conn.QuerySingleOrDefaultAsync(@"
                INSERT INTO template_items (template_id, criterion_id, value)
                VALUES (@TemplateId, @CriterionId, @Value::jsonb)
                RETURNING id, template_id, criterion_id, value, created_at, updated_at",
                p =>
                {
                    p.AddWithValue("TemplateId", item.TemplateId);
                    p.AddWithValue("CriterionId", item.CriterionId);
                    p.AddWithValue("Value", item.Value);
                },
                r => new TemplateItem
                {
                    Id = r.GetGuid(0),
                    TemplateId = r.GetGuid(1),
                    CriterionId = r.GetGuid(2),
                    Value = r.GetString(3),
                    CreatedAt = r.GetDateTime(4),
                    UpdatedAt = r.GetDateTime(5)
                }, ct))!;
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ConflictException($"Template already has this criterion: {item.CriterionId}");
        }
    }

    public async Task<bool> DeleteTemplateItemAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        return await conn.ExecuteAsync("DELETE FROM template_items WHERE id = @Id",
            p => p.AddWithValue("Id", id), ct) > 0;
    }
}

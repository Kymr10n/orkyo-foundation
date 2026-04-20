using Npgsql;
using System.Text.Json;
using Api.Helpers;
using Api.Models;
using Api.Services;

namespace Api.Repositories;

public interface ITemplateRepository
{
    Task<List<Template>> GetAllAsync(string entityType);
    Task<Template?> GetByIdAsync(Guid id);
    Task<Template> CreateAsync(CreateTemplateRequest request);
    Task<Template?> UpdateAsync(Guid id, UpdateTemplateRequest request);
    Task<bool> DeleteAsync(Guid id);
    Task<List<TemplateItem>> GetTemplateItemsAsync(Guid templateId);
    Task<TemplateItem> CreateTemplateItemAsync(TemplateItem item);
    Task<bool> DeleteTemplateItemAsync(Guid id);
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

    private static Template MapTemplate(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        Name = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        EntityType = reader.GetString(3),
        DurationValue = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        DurationUnit = reader.IsDBNull(5) ? null : reader.GetString(5),
        FixedStart = reader.GetBoolean(6),
        FixedEnd = reader.GetBoolean(7),
        FixedDuration = reader.GetBoolean(8),
        CreatedAt = reader.GetDateTime(9),
        UpdatedAt = reader.GetDateTime(10)
    };

    public async Task<List<Template>> GetAllAsync(string entityType)
    {
        await using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                id,
                name,
                description,
                entity_type,
                duration_value,
                duration_unit,
                fixed_start,
                fixed_end,
                fixed_duration,
                created_at,
                updated_at
            FROM templates
            WHERE entity_type = @EntityType
            ORDER BY name
            LIMIT 500", conn);

        cmd.Parameters.AddWithValue("EntityType", entityType);

        var templates = new List<Template>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            templates.Add(MapTemplate(reader));
        }

        return templates;
    }

    public async Task<Template?> GetByIdAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                id,
                name,
                description,
                entity_type,
                duration_value,
                duration_unit,
                fixed_start,
                fixed_end,
                fixed_duration,
                created_at,
                updated_at
            FROM templates
            WHERE id = @Id", conn);

        cmd.Parameters.AddWithValue("Id", id);

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return MapTemplate(reader);
    }

    public async Task<Template> CreateAsync(CreateTemplateRequest request)
    {
        if (!ValidEntityTypes.Contains(request.EntityType))
        {
            throw new ArgumentException($"Invalid entity type: {request.EntityType}");
        }

        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO templates (
                name,
                description,
                entity_type,
                duration_value,
                duration_unit,
                fixed_start,
                fixed_end,
                fixed_duration
            ) VALUES (
                @Name,
                @Description,
                @EntityType,
                @DurationValue,
                @DurationUnit,
                @FixedStart,
                @FixedEnd,
                @FixedDuration
            ) RETURNING 
                id,
                name,
                description,
                entity_type,
                duration_value,
                duration_unit,
                fixed_start,
                fixed_end,
                fixed_duration,
                created_at,
                updated_at", conn);

        cmd.Parameters.AddWithValue("Name", request.Name);
        cmd.Parameters.AddWithValue("Description", (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("EntityType", request.EntityType);
        cmd.Parameters.AddWithValue("DurationValue", (object?)request.DurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("DurationUnit", (object?)request.DurationUnit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("FixedStart", request.FixedStart);
        cmd.Parameters.AddWithValue("FixedEnd", request.FixedEnd);
        cmd.Parameters.AddWithValue("FixedDuration", request.FixedDuration);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return MapTemplate(reader);
    }

    public async Task<Template?> UpdateAsync(Guid id, UpdateTemplateRequest request)
    {
        if (!ValidEntityTypes.Contains(request.EntityType))
        {
            throw new ArgumentException($"Invalid entity type: {request.EntityType}");
        }

        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            UPDATE templates SET 
                name = @Name,
                description = @Description,
                entity_type = @EntityType,
                duration_value = @DurationValue,
                duration_unit = @DurationUnit,
                fixed_start = @FixedStart,
                fixed_end = @FixedEnd,
                fixed_duration = @FixedDuration,
                updated_at = NOW()
            WHERE id = @Id
            RETURNING 
                id,
                name,
                description,
                entity_type,
                duration_value,
                duration_unit,
                fixed_start,
                fixed_end,
                fixed_duration,
                created_at,
                updated_at", conn);

        cmd.Parameters.AddWithValue("Id", id);
        cmd.Parameters.AddWithValue("Name", request.Name);
        cmd.Parameters.AddWithValue("Description", (object?)request.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("EntityType", request.EntityType);
        cmd.Parameters.AddWithValue("DurationValue", (object?)request.DurationValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("DurationUnit", (object?)request.DurationUnit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("FixedStart", request.FixedStart);
        cmd.Parameters.AddWithValue("FixedEnd", request.FixedEnd);
        cmd.Parameters.AddWithValue("FixedDuration", request.FixedDuration);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null; // Template not found
        }

        return MapTemplate(reader);
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("DELETE FROM templates WHERE id = @Id", conn);
        cmd.Parameters.AddWithValue("Id", id);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }

    // Template items methods
    public async Task<List<TemplateItem>> GetTemplateItemsAsync(Guid templateId)
    {
        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT 
                ti.id,
                ti.template_id,
                ti.criterion_id,
                ti.value,
                ti.created_at,
                ti.updated_at,
                c.name as criterion_name,
                c.data_type as criterion_data_type
            FROM template_items ti
            INNER JOIN criteria c ON ti.criterion_id = c.id
            WHERE ti.template_id = @TemplateId
            ORDER BY c.name", conn);

        cmd.Parameters.AddWithValue("TemplateId", templateId);

        var items = new List<TemplateItem>();
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            items.Add(new TemplateItem
            {
                Id = reader.GetGuid(0),
                TemplateId = reader.GetGuid(1),
                CriterionId = reader.GetGuid(2),
                Value = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5),
                CriterionName = reader.GetString(6),
                CriterionDataType = reader.GetString(7),
                CriterionCategory = null // Category not available in current schema
            });
        }

        return items;
    }

    public async Task<TemplateItem> CreateTemplateItemAsync(TemplateItem item)
    {
        // Validate template exists
        var template = await GetByIdAsync(item.TemplateId);
        if (template == null)
        {
            throw new ArgumentException($"Template not found: {item.TemplateId}");
        }

        using var conn = _connectionFactory.CreateOrgConnection(_orgContext);
        await conn.OpenAsync();

        // Validate criterion exists
        await using var checkCmd = new NpgsqlCommand(
            "SELECT EXISTS(SELECT 1 FROM criteria WHERE id = @CriterionId)", conn);
        checkCmd.Parameters.AddWithValue("CriterionId", item.CriterionId);

        var criterionExists = (bool?)await checkCmd.ExecuteScalarAsync();
        if (criterionExists != true)
        {
            throw new ArgumentException($"Criterion not found: {item.CriterionId}");
        }

        try
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO template_items (
                    template_id,
                    criterion_id,
                    value
                ) VALUES (
                    @TemplateId,
                    @CriterionId,
                    @Value::jsonb
                ) RETURNING 
                    id,
                    template_id,
                    criterion_id,
                    value,
                    created_at,
                    updated_at", conn);

            cmd.Parameters.AddWithValue("TemplateId", item.TemplateId);
            cmd.Parameters.AddWithValue("CriterionId", item.CriterionId);
            cmd.Parameters.AddWithValue("Value", item.Value);

            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();

            return new TemplateItem
            {
                Id = reader.GetGuid(0),
                TemplateId = reader.GetGuid(1),
                CriterionId = reader.GetGuid(2),
                Value = reader.GetString(3),
                CreatedAt = reader.GetDateTime(4),
                UpdatedAt = reader.GetDateTime(5)
            };
        }
        catch (PostgresException ex) when (ex.SqlState == "23505") // unique violation
        {
            throw new InvalidOperationException(
                $"Template already has this criterion: {item.CriterionId}");
        }
    }

    public async Task<bool> DeleteTemplateItemAsync(Guid id)
    {
        using var conn = _connectionFactory.CreateOrgConnection(_orgContext); await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM template_items WHERE id = @Id", conn);
        cmd.Parameters.AddWithValue("Id", id);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();
        return rowsAffected > 0;
    }
}

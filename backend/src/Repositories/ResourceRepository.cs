using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceRepository
{
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter);
    Task<ResourceInfo?> GetByIdAsync(Guid id);
    Task<ResourceInfo> CreateAsync(Guid resourceTypeId, string typeKey, string name, string? description, string? externalReference, string allocationMode, int baseAvailabilityPercent);
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request);
    Task<bool> DeactivateAsync(Guid id);
}

public class ResourceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceRepository
{
    private const string SelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.is_active, r.created_at, r.updated_at";

    public async Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var where = new List<string>();
        var cmd = new NpgsqlCommand();
        cmd.Connection = db;

        if (filter.ResourceTypeKey is not null)
        {
            where.Add("rt.key = @typeKey");
            cmd.Parameters.AddWithValue("typeKey", filter.ResourceTypeKey);
        }
        if (filter.IsActive.HasValue)
        {
            where.Add("r.is_active = @isActive");
            cmd.Parameters.AddWithValue("isActive", filter.IsActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add("r.name ILIKE @search");
            cmd.Parameters.AddWithValue("search", $"%{filter.Search}%");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        cmd.CommandText =
            $"SELECT {SelectColumns} FROM resources r " +
            $"JOIN resource_types rt ON r.resource_type_id = rt.id " +
            $"{whereClause} ORDER BY r.name LIMIT 1000";

        var result = new List<ResourceInfo>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(Map(reader));
        return result;
    }

    public async Task<ResourceInfo?> GetByIdAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            $"SELECT {SelectColumns} FROM resources r " +
            "JOIN resource_types rt ON r.resource_type_id = rt.id " +
            "WHERE r.id = @id", db);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? Map(reader) : null;
    }

    public async Task<ResourceInfo> CreateAsync(
        Guid resourceTypeId, string typeKey, string name, string? description,
        string? externalReference, string allocationMode, int baseAvailabilityPercent)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO resources
                (resource_type_id, name, description, external_reference,
                 allocation_mode, base_availability_percent)
            VALUES
                (@resourceTypeId, @name, @description, @externalReference,
                 @allocationMode, @baseAvailabilityPercent)
            RETURNING id, created_at, updated_at", db);

        cmd.Parameters.AddWithValue("resourceTypeId", resourceTypeId);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("externalReference", (object?)externalReference ?? DBNull.Value);
        cmd.Parameters.AddWithValue("allocationMode", allocationMode);
        cmd.Parameters.AddWithValue("baseAvailabilityPercent", baseAvailabilityPercent);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        var id = reader.GetGuid(reader.GetOrdinal("id"));
        var createdAt = reader.GetDateTime(reader.GetOrdinal("created_at"));
        var updatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"));

        return new ResourceInfo
        {
            Id = id,
            ResourceTypeId = resourceTypeId,
            ResourceTypeKey = typeKey,
            Name = name,
            Description = description,
            ExternalReference = externalReference,
            AllocationMode = allocationMode,
            BaseAvailabilityPercent = baseAvailabilityPercent,
            IsActive = true,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        var sets = new List<string> { "updated_at = NOW()" };
        var cmd = new NpgsqlCommand();
        cmd.Connection = db;
        cmd.Parameters.AddWithValue("id", id);

        if (request.Name is not null)
        {
            sets.Add("name = @name");
            cmd.Parameters.AddWithValue("name", request.Name);
        }
        if (request.Description is not null)
        {
            sets.Add("description = @description");
            cmd.Parameters.AddWithValue("description", request.Description);
        }
        if (request.ExternalReference is not null)
        {
            sets.Add("external_reference = @externalReference");
            cmd.Parameters.AddWithValue("externalReference", request.ExternalReference);
        }
        if (request.AllocationMode is not null)
        {
            sets.Add("allocation_mode = @allocationMode");
            cmd.Parameters.AddWithValue("allocationMode", request.AllocationMode);
        }
        if (request.BaseAvailabilityPercent.HasValue)
        {
            sets.Add("base_availability_percent = @basePct");
            cmd.Parameters.AddWithValue("basePct", request.BaseAvailabilityPercent.Value);
        }
        if (request.IsActive.HasValue)
        {
            sets.Add("is_active = @isActive");
            cmd.Parameters.AddWithValue("isActive", request.IsActive.Value);
        }

        cmd.CommandText =
            $"UPDATE resources SET {string.Join(", ", sets)} WHERE id = @id";
        await cmd.ExecuteNonQueryAsync();

        return await GetByIdAsync(id);
    }

    public async Task<bool> DeactivateAsync(Guid id)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "UPDATE resources SET is_active = false, updated_at = NOW() WHERE id = @id", db);
        cmd.Parameters.AddWithValue("id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static ResourceInfo Map(NpgsqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("id")),
        ResourceTypeId = r.GetGuid(r.GetOrdinal("resource_type_id")),
        ResourceTypeKey = r.GetString(r.GetOrdinal("resource_type_key")),
        Name = r.GetString(r.GetOrdinal("name")),
        Description = r.IsDBNull(r.GetOrdinal("description")) ? null : r.GetString(r.GetOrdinal("description")),
        ExternalReference = r.IsDBNull(r.GetOrdinal("external_reference")) ? null : r.GetString(r.GetOrdinal("external_reference")),
        AllocationMode = r.GetString(r.GetOrdinal("allocation_mode")),
        BaseAvailabilityPercent = r.GetInt32(r.GetOrdinal("base_availability_percent")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}

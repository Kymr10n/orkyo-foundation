using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceRepository
{
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default);
    Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResourceInfo> CreateAsync(Guid resourceTypeId, string typeKey, string name, string? description, string? externalReference, string allocationMode, int baseAvailabilityPercent, Guid? id = null, CancellationToken ct = default);
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default);
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public class ResourceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceRepository
{
    private const string SelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.is_active, r.created_at, r.updated_at";

    public async Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

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
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return result;
    }

    public async Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} FROM resources r " +
            "JOIN resource_types rt ON r.resource_type_id = rt.id " +
            "WHERE r.id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<ResourceInfo> CreateAsync(
        Guid resourceTypeId, string typeKey, string name, string? description,
        string? externalReference, string allocationMode, int baseAvailabilityPercent, Guid? id = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var insertedId = id ?? Guid.NewGuid();

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO resources
                (id, resource_type_id, name, description, external_reference,
                 allocation_mode, base_availability_percent)
            VALUES
                (@id, @resourceTypeId, @name, @description, @externalReference,
                 @allocationMode, @baseAvailabilityPercent)
            RETURNING id, created_at, updated_at",
            p =>
            {
                p.AddWithValue("id", insertedId);
                p.AddWithValue("resourceTypeId", resourceTypeId);
                p.AddWithValue("name", name);
                p.AddNullable("description", description);
                p.AddNullable("externalReference", externalReference);
                p.AddWithValue("allocationMode", allocationMode);
                p.AddWithValue("baseAvailabilityPercent", baseAvailabilityPercent);
            },
            r => new ResourceInfo
            {
                Id = r.GetGuid(r.GetOrdinal("id")),
                ResourceTypeId = resourceTypeId,
                ResourceTypeKey = typeKey,
                Name = name,
                Description = description,
                ExternalReference = externalReference,
                AllocationMode = allocationMode,
                BaseAvailabilityPercent = baseAvailabilityPercent,
                IsActive = true,
                CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
                UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
            }, ct))!;
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        var update = new UpdateBuilder().SetExpression("updated_at = NOW()");
        update.SetIfNotNull("name", request.Name);
        update.SetIfNotNull("description", request.Description);
        update.SetIfNotNull("external_reference", request.ExternalReference);
        update.SetIfNotNull("allocation_mode", request.AllocationMode);
        if (request.BaseAvailabilityPercent.HasValue)
            update.Set("base_availability_percent", request.BaseAvailabilityPercent.Value);
        if (request.IsActive.HasValue)
            update.Set("is_active", request.IsActive.Value);

        await db.ExecuteAsync($"UPDATE resources SET {update.SetClause} WHERE id = @id",
            p => { p.AddWithValue("id", id); update.Apply(p); }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.ExecuteAsync(
            "UPDATE resources SET is_active = false, updated_at = NOW() WHERE id = @id",
            p => p.AddWithValue("id", id), ct) > 0;
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

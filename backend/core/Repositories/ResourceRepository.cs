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
    Task<List<ResourceInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task<ResourceInfo> CreateAsync(Guid resourceTypeId, string typeKey, string name, string? description, string? externalReference, string allocationMode, int baseAvailabilityPercent, Guid? homeSiteId = null, bool crossSiteAllowed = true, Guid? id = null, CancellationToken ct = default);
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default);
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);
}

public class ResourceRepository(OrgContext orgContext, IOrgDbConnectionFactory connectionFactory)
    : IResourceRepository
{
    // Derived "current site": where the resource is right now. A space is immovable
    // (spaces.site_id); a person/tool is wherever a non-cancelled assignment overlapping now()
    // places them, else their home site. Read-only — never stored. On concurrent (fractional)
    // assignments the most recently started one wins, so the value is deterministic.
    private const string CurrentSiteExpr =
        @"COALESCE(
            (SELECT sp.site_id FROM spaces sp WHERE sp.id = r.id),
            (SELECT req.site_id
               FROM resource_assignments ra
               JOIN requests req ON req.id = ra.request_id
              WHERE ra.resource_id = r.id
                AND ra.assignment_status <> 'Cancelled'
                AND ra.start_utc <= now() AND ra.end_utc > now()
                AND req.site_id IS NOT NULL
              ORDER BY ra.start_utc DESC
              LIMIT 1),
            r.home_site_id)";

    // Site membership over a window: home site matches, or a non-cancelled assignment to a request
    // at the site overlaps [@siteFrom, @siteTo). Mirrors the assignment→request→site join in
    // CurrentSiteExpr, but window-based (the People utilization grid filters by the visible window).
    private const string SiteWindowMembershipExpr =
        @"(r.home_site_id = @siteId
           OR EXISTS (SELECT 1 FROM resource_assignments ra
                        JOIN requests req ON req.id = ra.request_id
                       WHERE ra.resource_id = r.id
                         AND ra.assignment_status <> 'Cancelled'
                         AND ra.start_utc < @siteTo AND ra.end_utc > @siteFrom
                         AND req.site_id = @siteId))";

    private const string SelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.home_site_id, " + CurrentSiteExpr + " AS current_site_id, r.cross_site_allowed, " +
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
        if (filter.SiteId.HasValue)
        {
            cmd.Parameters.AddWithValue("siteId", filter.SiteId.Value);
            if (filter.SiteWindowFrom.HasValue && filter.SiteWindowTo.HasValue)
            {
                where.Add(SiteWindowMembershipExpr);
                cmd.Parameters.AddWithValue("siteFrom", filter.SiteWindowFrom.Value);
                cmd.Parameters.AddWithValue("siteTo", filter.SiteWindowTo.Value);
            }
            else
            {
                // No window → fall back to the as-of-now current site.
                where.Add($"(r.home_site_id = @siteId OR {CurrentSiteExpr} = @siteId)");
            }
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

    public async Task<List<ResourceInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} FROM resources r " +
            "JOIN resource_types rt ON r.resource_type_id = rt.id " +
            "WHERE r.id = ANY(@ids)",
            p => p.AddWithValue("ids", ids.ToArray()), Map, ct);
    }

    public async Task<ResourceInfo> CreateAsync(
        Guid resourceTypeId, string typeKey, string name, string? description,
        string? externalReference, string allocationMode, int baseAvailabilityPercent,
        Guid? homeSiteId = null, bool crossSiteAllowed = true,
        Guid? id = null, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var insertedId = id ?? Guid.NewGuid();

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO resources
                (id, resource_type_id, name, description, external_reference,
                 allocation_mode, base_availability_percent,
                 home_site_id, cross_site_allowed)
            VALUES
                (@id, @resourceTypeId, @name, @description, @externalReference,
                 @allocationMode, @baseAvailabilityPercent,
                 @homeSiteId, @crossSiteAllowed)
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
                p.AddNullable("homeSiteId", homeSiteId);
                p.AddWithValue("crossSiteAllowed", crossSiteAllowed);
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
                HomeSiteId = homeSiteId,
                // A freshly created resource has no assignments yet, so it is at its home site.
                CurrentSiteId = homeSiteId,
                CrossSiteAllowed = crossSiteAllowed,
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
        if (request.HomeSiteId.HasValue) update.Set("home_site_id", request.HomeSiteId.Value);
        if (request.CrossSiteAllowed.HasValue) update.Set("cross_site_allowed", request.CrossSiteAllowed.Value);

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
        HomeSiteId = r.IsDBNull(r.GetOrdinal("home_site_id")) ? null : r.GetGuid(r.GetOrdinal("home_site_id")),
        CurrentSiteId = r.IsDBNull(r.GetOrdinal("current_site_id")) ? null : r.GetGuid(r.GetOrdinal("current_site_id")),
        CrossSiteAllowed = r.GetBoolean(r.GetOrdinal("cross_site_allowed")),
        IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
    };
}

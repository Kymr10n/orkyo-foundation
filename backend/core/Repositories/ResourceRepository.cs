using System.Text.Json;
using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Security.Encryption;
using Api.Services;
using Npgsql;

namespace Api.Repositories;

public interface IResourceRepository
{
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default);
    Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ResourceInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    /// <summary>
    /// Inserts a resource, placement columns included — one statement, so a placeable resource
    /// can never exist without the site and geometry that make it placed.
    /// Throws <see cref="Helpers.ConflictException"/> when the code is taken at the home site.
    /// </summary>
    Task<ResourceInfo> CreateAsync(Guid resourceTypeId, CreateResourceRequest request, CancellationToken ct = default);
    Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default);
    /// <summary>Deactivates an active resource. Returns <c>false</c> when it is unknown or already
    /// inactive, so a caller can act on the transition exactly once.</summary>
    Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default);

    // ── Placeable resources ───────────────────────────────────────────────────
    // Reads scoped to types that declare geometry. They order by code before name and resolve the
    // site from home_site_id alone: a placeable resource cannot travel, so the derived current
    // site of the generic list filter would only ever repeat its home site.

    // The single-site reads that used to live here went with the space routes. Callers ask the
    // generic list for `hasGeometry=true&isActive=true&siteId=`, which selects the same rows.

    /// <summary>Bulk fetch for many sites in one query, keyed by site id — for export.</summary>
    Task<Dictionary<Guid, List<ResourceInfo>>> GetPlaceableBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default);
    /// <summary>Exact count of active placeable resources across the tenant — the "spaces" quota usage.</summary>
    Task<int> GetPlaceableCountAsync(CancellationToken ct = default);
}

public class ResourceRepository(
    OrgContext orgContext,
    IOrgDbConnectionFactory connectionFactory,
    IEncryptionService encryption)
    : IResourceRepository
{
    // Derived "current site": where the resource is right now — wherever a non-cancelled
    // assignment overlapping now() places it, else its home site. Read-only — never stored. On
    // concurrent (fractional) assignments the most recently started one wins, so the value is
    // deterministic.
    //
    // The assignment arm applies only to resources allowed to travel. An immovable resource
    // always reports its home site: it cannot be somewhere else, so an assignment to a request
    // filed against another site says something about the request, not about the resource.
    // This is what the spaces table used to express by keeping a space's site in its own
    // column where no assignment could override it.
    private const string CurrentSiteExpr =
        $@"COALESCE(
            CASE WHEN r.cross_site_allowed THEN
            (SELECT req.site_id
               FROM resource_assignments ra
               JOIN requests req ON req.id = ra.request_id
              WHERE ra.resource_id = r.id
                AND ra.assignment_status <> '{AssignmentStatuses.Cancelled}'
                AND ra.start_utc <= now() AND ra.end_utc > now()
                AND req.site_id IS NOT NULL
              ORDER BY ra.start_utc DESC
              LIMIT 1) END,
            r.home_site_id)";

    // Site membership over a window: home site matches, or a non-cancelled assignment to a request
    // at the site overlaps [@siteFrom, @siteTo). Mirrors the assignment→request→site join in
    // CurrentSiteExpr, but window-based (the People utilization grid filters by the visible window).
    private const string SiteWindowMembershipExpr =
        $@"(r.home_site_id = @siteId
           OR EXISTS (SELECT 1 FROM resource_assignments ra
                        JOIN requests req ON req.id = ra.request_id
                       WHERE ra.resource_id = r.id
                         AND ra.assignment_status <> '{AssignmentStatuses.Cancelled}'
                         AND ra.start_utc < @siteTo AND ra.end_utc > @siteFrom
                         AND req.site_id = @siteId))";

    // Group membership lives in resource_group_members, and a scalar subquery rather than a LEFT
    // JOIN because a join multiplies rows for any type that allows more than one group — which
    // would inflate both a list and the COUNT beside it. Placeable types declare
    // single_group_membership, so for them there is at most one row to pick from anyway.
    private const string GroupIdExpr =
        "(SELECT m.resource_group_id FROM resource_group_members m WHERE m.resource_id = r.id LIMIT 1)";

    private const string SelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.home_site_id, " + CurrentSiteExpr + " AS current_site_id, r.cross_site_allowed, " +
        "r.code, r.is_physical, r.geometry, r.properties, r.capacity, r.custom_fields, " +
        // Directory columns. email is CITEXT and Npgsql has no handler for it, so it is cast —
        // the same cast PersonProfileRepository makes.
        "r.email::text AS email, r.job_title_id, r.department_id, r.linked_user_id, r.notes, " +
        GroupIdExpr + " AS group_id, " +
        "r.is_active, r.created_at, r.updated_at";

    private const string FromClause =
        "FROM resources r JOIN resource_types rt ON rt.id = r.resource_type_id";

    // What used to be `key = 'space'` plus the existence of a spaces row: deleting a space
    // deactivated its resource, so an inactive one was already invisible to these reads.
    private const string PlaceableScope = "rt.has_geometry AND r.is_active";

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
        if (filter.HasGeometry.HasValue)
        {
            where.Add("rt.has_geometry = @hasGeometry");
            cmd.Parameters.AddWithValue("hasGeometry", filter.HasGeometry.Value);
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

    public async Task<ResourceInfo> CreateAsync(Guid resourceTypeId, CreateResourceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // A code identifies a resource within its site, so the clash is per-site. Enforced here
        // rather than by a unique index because an index cannot see has_geometry, which is a
        // property of the type, and codes are only meaningful for placeable types.
        if (request.Code is not null)
        {
            var taken = await db.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) {FromClause} WHERE {PlaceableScope} " +
                "AND r.home_site_id = @siteId AND r.code = @code",
                p =>
                {
                    p.AddNullable("siteId", request.HomeSiteId);
                    p.AddWithValue("code", request.Code);
                }, ct) > 0;
            if (taken)
                throw new ConflictException($"Code '{request.Code}' already exists for this site");
        }

        // properties and custom_fields are NOT NULL: an absent object is an empty one, never a SQL NULL.
        var geometryJson = request.Geometry is null ? null : JsonSerializer.Serialize(request.Geometry);
        var propertiesJson = request.Properties is null ? "{}" : JsonSerializer.Serialize(request.Properties);
        var customFieldsJson = request.CustomFields is null ? "{}" : JsonSerializer.Serialize(request.CustomFields);
        var insertedId = Guid.NewGuid();

        return (await db.QuerySingleOrDefaultAsync(@"
            INSERT INTO resources
                (id, resource_type_id, name, description, external_reference,
                 allocation_mode, base_availability_percent,
                 home_site_id, cross_site_allowed,
                 code, is_physical, geometry, properties, capacity, custom_fields,
                 email, job_title_id, department_id, notes)
            VALUES
                (@id, @resourceTypeId, @name, @description, @externalReference,
                 @allocationMode, @baseAvailabilityPercent,
                 @homeSiteId, @crossSiteAllowed,
                 @code, @isPhysical, @geometry, @properties, @capacity, @customFields,
                 @email, @jobTitleId, @departmentId, @notes)
            RETURNING id, created_at, updated_at",
            p =>
            {
                p.AddWithValue("id", insertedId);
                p.AddWithValue("resourceTypeId", resourceTypeId);
                p.AddWithValue("name", request.Name);
                p.AddNullable("description", request.Description);
                p.AddNullable("externalReference", request.ExternalReference);
                p.AddWithValue("allocationMode", request.AllocationMode);
                p.AddWithValue("baseAvailabilityPercent", request.BaseAvailabilityPercent);
                p.AddNullable("homeSiteId", request.HomeSiteId);
                p.AddWithValue("crossSiteAllowed", request.CrossSiteAllowed);
                p.AddNullable("code", request.Code);
                p.AddWithValue("isPhysical", request.IsPhysical);
                p.AddJsonb("geometry", geometryJson);
                p.AddJsonb("properties", propertiesJson);
                p.AddWithValue("capacity", request.Capacity);
                p.AddJsonb("customFields", customFieldsJson);
                p.AddNullable("email", request.Email);
                p.AddNullable("jobTitleId", request.JobTitleId);
                p.AddNullable("departmentId", request.DepartmentId);
                // Encrypted on the way in, exactly as PersonProfileRepository does it — the
                // column holds ciphertext and nothing else may write plaintext into it.
                p.AddNullable("notes", encryption.ProtectString(request.Notes, orgContext.OrgId));
            },
            r => new ResourceInfo
            {
                Id = r.GetGuid(r.GetOrdinal("id")),
                ResourceTypeId = resourceTypeId,
                ResourceTypeKey = request.ResourceTypeKey,
                Name = request.Name,
                Description = request.Description,
                ExternalReference = request.ExternalReference,
                AllocationMode = request.AllocationMode,
                BaseAvailabilityPercent = request.BaseAvailabilityPercent,
                HomeSiteId = request.HomeSiteId,
                // A freshly created resource has no assignments yet, so it is at its home site.
                CurrentSiteId = request.HomeSiteId,
                CrossSiteAllowed = request.CrossSiteAllowed,
                Code = request.Code,
                IsPhysical = request.IsPhysical,
                Geometry = request.Geometry,
                Properties = request.Properties,
                Capacity = request.Capacity,
                // Matches how Map reads it back: an empty document is reported as no values.
                CustomFields = request.CustomFields is { Count: > 0 } ? request.CustomFields : null,
                Email = request.Email,
                JobTitleId = request.JobTitleId,
                DepartmentId = request.DepartmentId,
                // The plaintext the caller sent, not the ciphertext just stored.
                Notes = request.Notes,
                // Membership is managed by the resource-group members editor, never at create time.
                GroupId = null,
                IsActive = true,
                CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
                UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
            }, ct))!;
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // No updated_at: the resources_updated_at BEFORE UPDATE trigger maintains it for every
        // statement that reaches this table, including the ones written elsewhere.
        var geometryJson = request.Geometry is null ? null : JsonSerializer.Serialize(request.Geometry);
        var propertiesJson = request.Properties is null ? null : JsonSerializer.Serialize(request.Properties);
        var customFieldsJson = request.CustomFields is null ? null : JsonSerializer.Serialize(request.CustomFields);

        var update = new UpdateBuilder();
        update.SetIfNotNull("name", request.Name);
        update.SetIfNotNull("description", request.Description);
        update.SetIfNotNull("external_reference", request.ExternalReference);
        update.SetIfNotNull("allocation_mode", request.AllocationMode);
        if (request.BaseAvailabilityPercent.HasValue)
            update.Set("base_availability_percent", request.BaseAvailabilityPercent.Value);
        if (request.IsActive.HasValue)
            update.Set("is_active", request.IsActive.Value);
        update.SetIfPresent("home_site_id", request.HomeSiteId);
        if (request.CrossSiteAllowed.HasValue) update.Set("cross_site_allowed", request.CrossSiteAllowed.Value);
        update.SetIfNotNull("code", request.Code);
        // Bound by hand rather than through the builder so the parameter carries its jsonb type.
        if (geometryJson is not null) update.SetExpression("geometry = @geometry");
        if (propertiesJson is not null) update.SetExpression("properties = @properties");
        if (customFieldsJson is not null) update.SetExpression("custom_fields = @customFields");
        if (request.Capacity.HasValue) update.Set("capacity", request.Capacity.Value);
        update.SetIfNotNull("email", request.Email);
        update.SetIfPresent("job_title_id", request.JobTitleId);
        update.SetIfPresent("department_id", request.DepartmentId);
        // Encrypted before it reaches the SET clause, so no write path can put plaintext in the
        // column even by accident.
        if (request.Notes is not null)
            update.Set("notes", encryption.ProtectString(request.Notes, orgContext.OrgId)!);

        if (update.IsEmpty) return await GetByIdAsync(id, ct);

        await db.ExecuteAsync($"UPDATE resources SET {update.SetClause} WHERE id = @id",
            p =>
            {
                p.AddWithValue("id", id);
                update.Apply(p);
                if (geometryJson is not null) p.AddJsonb("geometry", geometryJson);
                if (propertiesJson is not null) p.AddJsonb("properties", propertiesJson);
                if (customFieldsJson is not null) p.AddJsonb("customFields", customFieldsJson);
            }, ct);

        return await GetByIdAsync(id, ct);
    }

    public async Task<bool> DeactivateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        // AND is_active makes the result mean "this call deactivated it", which is what the
        // quota rollup needs: deactivating twice must not decrement twice.
        return await db.ExecuteAsync(
            "UPDATE resources SET is_active = false WHERE id = @id AND is_active",
            p => p.AddWithValue("id", id), ct) > 0;
    }

    // ── Placeable resources ───────────────────────────────────────────────────

    public async Task<Dictionary<Guid, List<ResourceInfo>>> GetPlaceableBySitesAsync(IReadOnlyList<Guid> siteIds, CancellationToken ct = default)
    {
        if (siteIds.Count == 0) return [];

        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        var rows = await db.QueryListAsync(
            $"SELECT {SelectColumns} {FromClause} WHERE {PlaceableScope} " +
            "AND r.home_site_id = ANY(@siteIds) ORDER BY r.home_site_id, r.code, r.name",
            p => p.AddWithValue("siteIds", siteIds.ToArray()), Map, ct);

        var map = new Dictionary<Guid, List<ResourceInfo>>();
        foreach (var row in rows)
        {
            if (!map.TryGetValue(row.HomeSiteId!.Value, out var list))
            {
                list = [];
                map[row.HomeSiteId.Value] = list;
            }
            list.Add(row);
        }
        return map;
    }

    public async Task<int> GetPlaceableCountAsync(CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return (int)await db.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) {FromClause} WHERE {PlaceableScope}", null, ct);
    }

    // Not static: it decrypts `notes`, which needs the injected service and the org id. Keeping
    // the decryption inside the one mapper every read path already uses is what stops a future
    // read path from returning ciphertext by omission.
    private ResourceInfo Map(NpgsqlDataReader r)
    {
        // The columns are jsonb; the DTO is typed. An empty properties object is reported as no
        // properties at all, which is what the space shape has always published.
        var geometryJson = r.GetNullableString("geometry");
        var propertiesJson = r.GetNullableString("properties") ?? "{}";
        var customFieldsJson = r.GetNullableString("custom_fields") ?? "{}";

        return new ResourceInfo
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
            Code = r.GetNullableString("code"),
            IsPhysical = r.GetBoolean("is_physical"),
            Geometry = geometryJson is null ? null : JsonSerializer.Deserialize<ResourceGeometry>(geometryJson),
            Properties = propertiesJson == "{}" ? null : JsonSerializer.Deserialize<Dictionary<string, object>>(propertiesJson),
            Capacity = r.GetInt32("capacity"),
            GroupId = r.GetNullableGuid("group_id"),
            CustomFields = customFieldsJson == "{}"
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(customFieldsJson),
            Email = r.GetNullableString("email"),
            JobTitleId = r.GetNullableGuid("job_title_id"),
            DepartmentId = r.GetNullableGuid("department_id"),
            LinkedUserId = r.GetNullableGuid("linked_user_id"),
            // Decrypted here rather than by the caller: every read path goes through this mapper,
            // so a caller that forgot would hand ciphertext to a client.
            Notes = encryption.UnprotectString(r.GetNullableString("notes"), orgContext.OrgId),
            IsActive = r.GetBoolean(r.GetOrdinal("is_active")),
            CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
            UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
        };
    }
}

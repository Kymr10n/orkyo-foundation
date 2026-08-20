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
    /// <summary>
    /// One capped page of the filtered list — at most 1000 rows, no total, no signal when it cut.
    /// Right for a list view; wrong wherever the answer has to be complete. See
    /// <see cref="GetEveryAsync"/>.
    /// </summary>
    Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default);

    /// <summary>
    /// Every resource matching the filter, read in pages until the total is reached.
    /// </summary>
    /// <remarks>
    /// For callers that aggregate, export or schedule, where <see cref="GetAllAsync"/>'s cap
    /// would not shorten a list but produce a wrong number — a utilization figure computed over
    /// the first 1000 of 1200 resources is not a partial answer, it is an incorrect one, and
    /// nothing in the response would say so.
    /// </remarks>
    Task<List<ResourceInfo>> GetEveryAsync(ResourceListFilter filter, CancellationToken ct = default);
    /// <summary>One page of the filtered list plus the unpaged total, in one connection.
    /// The caller clamps <paramref name="limit"/>/<paramref name="offset"/>.</summary>
    Task<(List<ResourceInfo> Items, int Total)> GetPageAsync(
        ResourceListFilter filter, int limit, int offset, CancellationToken ct = default);
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
    // assignment overlapping now() places it, else its home site (the COALESCE lives in the
    // projection). Read-only — never stored. On concurrent (fractional) assignments the most
    // recently started one wins, so the value is deterministic.
    //
    // The assignment arm applies only to resources allowed to travel (the cross_site_allowed
    // predicate inside the lateral). An immovable resource always reports its home site: it
    // cannot be somewhere else, so an assignment to a request filed against another site says
    // something about the request, not about the resource. This is what the spaces table used
    // to express by keeping a space's site in its own column where no assignment could
    // override it.
    //
    // A lateral rather than a scalar subquery in the SELECT list: the same value is needed by
    // the site filter, and as a lateral it is computed once per row instead of once per
    // reference. LIMIT 1 means it cannot multiply rows.
    private const string CurrentSiteLateral =
        $@" LEFT JOIN LATERAL (
            SELECT req.site_id
              FROM resource_assignments ra
              JOIN requests req ON req.id = ra.request_id
             WHERE r.cross_site_allowed
               AND ra.resource_id = r.id
               AND ra.assignment_status <> '{AssignmentStatuses.Cancelled}'
               AND ra.start_utc <= now() AND ra.end_utc > now()
               AND req.site_id IS NOT NULL
             ORDER BY ra.start_utc DESC
             LIMIT 1) cs ON TRUE";

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

    // Group membership lives in resource_group_members. A LIMIT 1 lateral rather than a plain
    // LEFT JOIN because a join multiplies rows for any type that allows more than one group —
    // which would inflate both a list and the COUNT beside it. Placeable types declare
    // single_group_membership, so for them there is at most one row to pick from anyway.
    private const string GroupLateral =
        @" LEFT JOIN LATERAL (
            SELECT m.resource_group_id FROM resource_group_members m
             WHERE m.resource_id = r.id LIMIT 1) gm ON TRUE";

    private const string SelectColumns =
        "r.id, r.resource_type_id, rt.key as resource_type_key, r.name, r.description, " +
        "r.external_reference, r.allocation_mode, r.base_availability_percent, " +
        "r.home_site_id, COALESCE(cs.site_id, r.home_site_id) AS current_site_id, r.cross_site_allowed, " +
        "r.code, r.is_physical, r.geometry, r.properties, r.capacity, r.custom_fields, " +
        // Directory columns. email is CITEXT and Npgsql has no handler for it, so it is cast —
        // the same cast PersonProfileRepository makes.
        "r.email::text AS email, r.linked_user_id, r.notes, " +
        "gm.resource_group_id AS group_id, " +
        "r.is_active, r.created_at, r.updated_at";

    private const string FromClause =
        "FROM resources r JOIN resource_types rt ON rt.id = r.resource_type_id";

    // The FROM every SelectColumns read uses: base join plus the two laterals the projection
    // references. Writes and counts that do not project SelectColumns keep FromClause.
    private const string ReadFrom = FromClause + CurrentSiteLateral + GroupLateral;

    // What used to be `key = 'space'` plus the existence of a spaces row: deleting a space
    // deactivated its resource, so an inactive one was already invisible to these reads.
    private const string PlaceableScope = "rt.has_geometry AND r.is_active";

    // Shared WHERE assembly for the list reads. UsesCurrentSite reports whether a fragment
    // references the cs lateral — a COUNT over such a WHERE must use ReadFrom, not FromClause.
    private static (List<string> Where, bool UsesCurrentSite) BuildFilter(
        ResourceListFilter filter, NpgsqlParameterCollection p)
    {
        var where = new List<string>();
        var usesCurrentSite = false;

        if (filter.ResourceTypeKey is not null)
        {
            where.Add("rt.key = @typeKey");
            p.AddWithValue("typeKey", filter.ResourceTypeKey);
        }
        if (filter.IsActive.HasValue)
        {
            where.Add("r.is_active = @isActive");
            p.AddWithValue("isActive", filter.IsActive.Value);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add("r.name ILIKE @search");
            p.AddWithValue("search", $"%{filter.Search}%");
        }
        if (filter.HasGeometry.HasValue)
        {
            where.Add("rt.has_geometry = @hasGeometry");
            p.AddWithValue("hasGeometry", filter.HasGeometry.Value);
        }
        if (filter.SiteId.HasValue)
        {
            p.AddWithValue("siteId", filter.SiteId.Value);
            if (filter.SiteWindowFrom.HasValue && filter.SiteWindowTo.HasValue)
            {
                where.Add(SiteWindowMembershipExpr);
                p.AddWithValue("siteFrom", filter.SiteWindowFrom.Value);
                p.AddWithValue("siteTo", filter.SiteWindowTo.Value);
            }
            else
            {
                // No window → fall back to the as-of-now current site, via the cs lateral so
                // the assignment scan runs once per row instead of once per reference.
                where.Add("(r.home_site_id = @siteId OR COALESCE(cs.site_id, r.home_site_id) = @siteId)");
                usesCurrentSite = true;
            }
        }

        return (where, usesCurrentSite);
    }

    public async Task<List<ResourceInfo>> GetAllAsync(ResourceListFilter filter, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        var cmd = new NpgsqlCommand();
        cmd.Connection = db;

        var (where, _) = BuildFilter(filter, cmd.Parameters);
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        cmd.CommandText = $"SELECT {SelectColumns} {ReadFrom} {whereClause} ORDER BY r.name LIMIT 1000";

        var result = new List<ResourceInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(Map(reader));
        return result;
    }

    // Hand-rolled count + page on one connection rather than QueryPagedAsync: PageRequest
    // clamps page sizes to 100, and the endpoint's unpaged branch serves up to 1000 rows
    // through this same method. The clamp belongs to the caller, not here.
    public async Task<(List<ResourceInfo> Items, int Total)> GetPageAsync(
        ResourceListFilter filter, int limit, int offset, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        await db.OpenAsync(ct);

        var countCmd = new NpgsqlCommand();
        countCmd.Connection = db;
        var (where, usesCurrentSite) = BuildFilter(filter, countCmd.Parameters);
        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        // The laterals cannot multiply rows (LIMIT 1), so counting over ReadFrom is exact;
        // skipping them when nothing references cs is what makes the count cheap.
        countCmd.CommandText = $"SELECT COUNT(*) {(usesCurrentSite ? ReadFrom : FromClause)} {whereClause}";
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        var pageCmd = new NpgsqlCommand();
        pageCmd.Connection = db;
        BuildFilter(filter, pageCmd.Parameters);
        pageCmd.Parameters.AddWithValue("limit", limit);
        pageCmd.Parameters.AddWithValue("offset", offset);
        // r.id tiebreak keeps pages stable when names repeat.
        pageCmd.CommandText =
            $"SELECT {SelectColumns} {ReadFrom} {whereClause} ORDER BY r.name, r.id LIMIT @limit OFFSET @offset";

        var items = new List<ResourceInfo>();
        await using var reader = await pageCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(Map(reader));
        return (items, total);
    }

    public async Task<List<ResourceInfo>> GetEveryAsync(
        ResourceListFilter filter, CancellationToken ct = default)
    {
        // Pages through GetPageAsync rather than lifting GetAllAsync's LIMIT: same query, same
        // stable ORDER BY, and the total it already reports is what ends the loop.
        const int pageSize = 500;
        var all = new List<ResourceInfo>();
        while (true)
        {
            var (items, total) = await GetPageAsync(filter, pageSize, all.Count, ct);
            all.AddRange(items);
            // The count is the terminator; the empty page covers a delete landing mid-read.
            if (items.Count == 0 || all.Count >= total) return all;
        }
    }

    public async Task<ResourceInfo?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QuerySingleOrDefaultAsync(
            $"SELECT {SelectColumns} {ReadFrom} WHERE r.id = @id",
            p => p.AddWithValue("id", id), Map, ct);
    }

    public async Task<List<ResourceInfo>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        await using var db = connectionFactory.CreateOrgConnection(orgContext);
        return await db.QueryListAsync(
            $"SELECT {SelectColumns} {ReadFrom} WHERE r.id = ANY(@ids)",
            p => p.AddWithValue("ids", ids.ToArray()), Map, ct);
    }

    public async Task<ResourceInfo> CreateAsync(Guid resourceTypeId, CreateResourceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        if (request.Code is not null)
            await ThrowIfCodeTakenAsync(db, request.HomeSiteId, request.Code, excludeId: null, ct);

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
                 email, notes)
            VALUES
                (@id, @resourceTypeId, @name, @description, @externalReference,
                 @allocationMode, @baseAvailabilityPercent,
                 @homeSiteId, @crossSiteAllowed,
                 @code, @isPhysical, @geometry, @properties, @capacity, @customFields,
                 @email, @notes)
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
                // The plaintext the caller sent, not the ciphertext just stored.
                Notes = request.Notes,
                // Membership is managed by the resource-group members editor, never at create time.
                GroupId = null,
                IsActive = true,
                CreatedAt = r.GetDateTime(r.GetOrdinal("created_at")),
                UpdatedAt = r.GetDateTime(r.GetOrdinal("updated_at")),
            }, ct))!;
    }

    /// <summary>
    /// Refuses a code already used at the same site.
    /// </summary>
    /// <remarks>
    /// A code identifies a resource within its site, so the clash is per-site. Enforced here rather
    /// than by a unique index because an index cannot see <c>has_geometry</c>, which is a property
    /// of the type, and codes are only meaningful for placeable types.
    /// <para>
    /// A site-less resource is not checked: <c>home_site_id = NULL</c> is never true, and a code
    /// with no site to be unique within has nothing to clash with.
    /// </para>
    /// </remarks>
    private static async Task ThrowIfCodeTakenAsync(
        NpgsqlConnection db, Guid? siteId, string code, Guid? excludeId, CancellationToken ct)
    {
        var taken = await db.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) {FromClause} WHERE {PlaceableScope} "
            + "AND r.home_site_id = @siteId AND r.code = @code "
            + "AND (@excludeId::uuid IS NULL OR r.id <> @excludeId)",
            p =>
            {
                p.AddNullable("siteId", siteId);
                p.AddWithValue("code", code);
                p.AddNullable("excludeId", excludeId);
            }, ct) > 0;

        if (taken) throw new ConflictException($"Code '{code}' already exists for this site");
    }

    public async Task<ResourceInfo?> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken ct = default)
    {
        await using var db = connectionFactory.CreateOrgConnection(orgContext);

        // No updated_at: the resources_updated_at BEFORE UPDATE trigger maintains it for every
        // statement that reaches this table, including the ones written elsewhere.
        var geometryJson = request.Geometry is null ? null : JsonSerializer.Serialize(request.Geometry);
        var propertiesJson = request.Properties is null ? null : JsonSerializer.Serialize(request.Properties);
        var customFieldsJson = request.CustomFields is null ? null : JsonSerializer.Serialize(request.CustomFields);

        // Create rejects a taken code; an update that did not would be the way around it. The site
        // to check against is the one the resource will have, which this request may be changing.
        if (request.Code is not null || request.HomeSiteId.IsPresent)
        {
            var existing = await db.QuerySingleOrDefaultAsync(
                "SELECT code, home_site_id FROM resources WHERE id = @id",
                p => p.AddWithValue("id", id),
                r => (Code: r.IsDBNull(0) ? null : r.GetString(0),
                      SiteId: r.IsDBNull(1) ? (Guid?)null : r.GetGuid(1)), ct);

            var code = request.Code ?? existing.Code;
            var siteId = request.HomeSiteId.IsPresent ? request.HomeSiteId.Value : existing.SiteId;
            if (code is not null)
                await ThrowIfCodeTakenAsync(db, siteId, code, excludeId: id, ct);
        }

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
        // Encrypted before it reaches the SET clause, so no write path can put plaintext in the
        // column even by accident.
        if (request.Notes is not null)
            update.Set("notes", encryption.ProtectString(request.Notes, orgContext.OrgId)!);

        if (update.IsEmpty)
            return await db.QuerySingleOrDefaultAsync(
                $"SELECT {SelectColumns} {ReadFrom} WHERE r.id = @id",
                p => p.AddWithValue("id", id), Map, ct);

        // UPDATE and read-back in one statement, one connection: the CTE is aliased r so
        // SelectColumns and the laterals apply verbatim, and the BEFORE UPDATE trigger's
        // updated_at is already visible in RETURNING *. Zero CTE rows (unknown id) → null.
        return await db.QuerySingleOrDefaultAsync(
            $"WITH updated AS (UPDATE resources SET {update.SetClause} WHERE id = @id RETURNING *) " +
            $"SELECT {SelectColumns} FROM updated r " +
            "JOIN resource_types rt ON rt.id = r.resource_type_id" +
            CurrentSiteLateral + GroupLateral,
            p =>
            {
                p.AddWithValue("id", id);
                update.Apply(p);
                if (geometryJson is not null) p.AddJsonb("geometry", geometryJson);
                if (propertiesJson is not null) p.AddJsonb("properties", propertiesJson);
                if (customFieldsJson is not null) p.AddJsonb("customFields", customFieldsJson);
            }, Map, ct);
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
            $"SELECT {SelectColumns} {ReadFrom} WHERE {PlaceableScope} " +
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

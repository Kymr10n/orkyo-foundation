using System.Text.Json;

namespace Api.Models;

public record ResourceTypeInfo
{
    public required Guid Id { get; init; }
    public required string Key { get; init; }
    /// <summary>Singular — labels one resource ("Edit Car", a request's Tool slot).</summary>
    public required string DisplayName { get; init; }
    /// <summary>Plural — labels a collection (sidebar entry, utilization tab, page title).</summary>
    public required string DisplayNamePlural { get; init; }
    public string? Description { get; init; }
    /// <summary>lucide-react icon name; the frontend falls back to a default when null or unrecognised.</summary>
    public string? Icon { get; init; }
    /// <summary>
    /// Resources of this type can be placed on a floorplan: they carry a code, geometry and a
    /// capacity, and a site owns them. Replaces the hard-coded key = 'space' test.
    /// </summary>
    public required bool HasGeometry { get; init; }
    /// <summary>
    /// Resources of this type carry directory details — email, job title, department, a linked
    /// user account. Replaces the hard-coded key = 'person' test.
    /// </summary>
    public required bool HasDirectoryProfile { get; init; }
    /// <summary>
    /// A resource of this type belongs to at most one group. Enforced in the database by
    /// enforce_single_group_membership().
    /// </summary>
    public required bool SingleGroupMembership { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record ResourceInfo
{
    public required Guid Id { get; init; }
    public required Guid ResourceTypeId { get; init; }
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public required string AllocationMode { get; init; }
    public required int BaseAvailabilityPercent { get; init; }
    public required bool IsActive { get; init; }

    // Location model. Spaces resolve their site via spaces.site_id and leave HomeSiteId null;
    // people/tools carry an administrative home site. CurrentSiteId is derived (read-only), not
    // stored: it is where the resource actually is right now.
    /// <summary>Administrative/owning site and idle-time anchor (null for spaces and un-remediated resources).</summary>
    public Guid? HomeSiteId { get; init; }
    /// <summary>Derived (read-only): where the resource is right now — the site of the non-cancelled
    /// assignment overlapping the current time, else the home site (spaces always resolve to their own site).</summary>
    public Guid? CurrentSiteId { get; init; }
    /// <summary>Whether the resource may be assigned to requests at another site.</summary>
    public bool CrossSiteAllowed { get; init; } = true;

    // Placement. Only types declaring HasGeometry can carry these; for every other type they
    // hold the column defaults, because a resource that cannot be put on a floorplan has no
    // code, no shape and no seats.
    /// <summary>Short identifier, unique within the resource's home site.</summary>
    public string? Code { get; init; }
    /// <summary>Occupies real floor area, so it must carry geometry (the DB CHECK enforces the pair).</summary>
    public bool IsPhysical { get; init; }
    public ResourceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public int Capacity { get; init; } = 1;
    /// <summary>The group this resource belongs to. Single-valued because placeable types declare
    /// SingleGroupMembership; for a type that does not, the read reports one arbitrary membership.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>
    /// Values for the resource type's custom fields, keyed by field key. Descriptive only —
    /// nothing here is matchable, and the solver never reads it (see migration 1770). Carries
    /// values for retired fields too, so an edit that round-trips the document keeps them.
    /// </summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }

    // Directory details. Only types declaring HasDirectoryProfile carry them; for every other
    // type they are null. The columns live on `resources` (migration 1700), so reading them here
    // adds no join and no cost — the resolved display names (job title, department path) are the
    // part that needs joins, and those stay on the person-profile projection.
    /// <summary>Lookup and display address. Stored CITEXT, so comparisons are case-insensitive.</summary>
    public string? Email { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    /// <summary>The user account this person signs in as, when one is linked.</summary>
    public Guid? LinkedUserId { get; init; }
    /// <summary>
    /// Confidential free text — encrypted at rest, decrypted on the way out. Never log it and
    /// never put it in an error message.
    /// </summary>
    public string? Notes { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateResourceTypeRequest
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string DisplayNamePlural { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public bool HasGeometry { get; init; }
    public bool HasDirectoryProfile { get; init; }
    public bool SingleGroupMembership { get; init; }
}

public record UpdateResourceTypeRequest
{
    public string? DisplayName { get; init; }
    public string? DisplayNamePlural { get; init; }
    /// <summary>NULL leaves the flag as it is. Rejected for system types, whose behaviour the
    /// product's own pages depend on.</summary>
    public bool? HasGeometry { get; init; }
    public bool? HasDirectoryProfile { get; init; }
    public bool? SingleGroupMembership { get; init; }
    public string? Description { get; init; }
    public string? Icon { get; init; }
    public bool? IsActive { get; init; }
}

public record CreateResourceRequest
{
    public required string ResourceTypeKey { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public required string AllocationMode { get; init; }
    public int BaseAvailabilityPercent { get; init; } = 100;

    public Guid? HomeSiteId { get; init; }
    public bool CrossSiteAllowed { get; init; } = true;

    // Placement — rejected unless the named type declares HasGeometry.
    public string? Code { get; init; }
    public bool IsPhysical { get; init; }
    public ResourceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public int Capacity { get; init; } = 1;

    /// <summary>Values for the type's custom fields. Absent is the same as empty: every field
    /// the type marks required must still be present, so a required field cannot be skipped
    /// by leaving the document out.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }

    // Directory details — rejected unless the named type declares HasDirectoryProfile, the same
    // way placement is rejected on a type that cannot be placed. LinkedUserId is absent on
    // purpose: linking a person to a user account is its own operation with its own checks, not
    // a field on a create form.
    public string? Email { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    /// <summary>Confidential free text — encrypted before it is stored.</summary>
    public string? Notes { get; init; }

}

public record UpdateResourceRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? ExternalReference { get; init; }
    public string? AllocationMode { get; init; }
    public int? BaseAvailabilityPercent { get; init; }
    public bool? IsActive { get; init; }

    public Guid? HomeSiteId { get; init; }
    public bool? CrossSiteAllowed { get; init; }

    // Placement — rejected unless the resource's type declares HasGeometry. IsPhysical is absent
    // deliberately: flipping it would have to add or remove geometry in the same statement to
    // satisfy resources_physical_has_geometry_check, so it is a create-time decision.
    public string? Code { get; init; }
    public ResourceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public int? Capacity { get; init; }

    /// <summary>Replaces the whole value document; null leaves it untouched. Clients that render
    /// the form must send back the values they did not show (retired fields), or those values are
    /// what "replaces" discards.</summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }

    // Directory details — rejected unless the resource's type declares HasDirectoryProfile.
    // Null means "not editing this field", as everywhere else in a patch request.
    public string? Email { get; init; }
    public Guid? JobTitleId { get; init; }
    public Guid? DepartmentId { get; init; }
    public string? Notes { get; init; }
}

public record ResourceListFilter
{
    public string? ResourceTypeKey { get; init; }
    public bool? IsActive { get; init; }
    public string? Search { get; init; }

    /// <summary>
    /// When set, restricts results to resources belonging to this site: home_site_id = SiteId, or
    /// (for people/tools) a non-cancelled assignment to a request at this site overlapping the
    /// [SiteWindowFrom, SiteWindowTo] window. With no window, falls back to the as-of-now current site.
    /// </summary>
    public Guid? SiteId { get; init; }
    public DateTime? SiteWindowFrom { get; init; }
    public DateTime? SiteWindowTo { get; init; }

    /// <summary>
    /// When set, restricts results to types that do (or do not) declare geometry — the placeable
    /// resources that can sit on a floorplan.
    ///
    /// With <see cref="SiteId"/>, this reproduces the site-scoped placeable read exactly. That is
    /// not obvious, because SiteId is the wider "home or current site" predicate: a placeable
    /// resource is created with cross_site_allowed = false and cannot be assigned away from its
    /// site, so its current site is always its home site and the wider predicate collapses to
    /// home_site_id = SiteId for these rows. A test pins the equivalence.
    /// </summary>
    public bool? HasGeometry { get; init; }
}

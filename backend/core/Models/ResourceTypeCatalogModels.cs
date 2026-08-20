using Api.Constants;

namespace Api.Models;

/// <summary>Tenant-side state of a catalog entry, derived by key.</summary>
public static class CatalogEntryStates
{
    /// <summary>A row with the entry's key exists and is active.</summary>
    public const string Active = "active";

    /// <summary>A row exists but is deactivated — the Hide path; data survives.</summary>
    public const string Inactive = "inactive";

    /// <summary>No row with the entry's key exists; activation creates one.</summary>
    public const string Absent = "absent";
}

/// <summary>One catalog entry joined with what the tenant made of it.</summary>
public record CatalogEntryInfo
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string DisplayNamePlural { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required CatalogCategory Category { get; init; }
    public required bool HasGeometry { get; init; }
    public required bool HasDirectoryProfile { get; init; }
    public required bool SingleGroupMembership { get; init; }
    /// <summary>Labels of the fields activation ships, in form order.</summary>
    public required IReadOnlyList<string> FieldLabels { get; init; }

    /// <summary>One of <see cref="CatalogEntryStates"/>.</summary>
    public required string State { get; init; }
    /// <summary>The tenant's row for this key, when one exists.</summary>
    public Guid? ResourceTypeId { get; init; }
    /// <summary>The tenant's current display name — activation adopts the row, renames survive.</summary>
    public string? TenantDisplayName { get; init; }
    public int ResourceCount { get; init; }
    public int RequestTargetCount { get; init; }
}

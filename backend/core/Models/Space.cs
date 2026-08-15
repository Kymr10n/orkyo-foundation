using System.Text.Json;
namespace Api.Models;

/// <summary>
/// Coordinate in 2D space for geometry definitions.
/// Represents absolute pixel coordinates on a floorplan.
/// </summary>
public record Coordinate
{
    public required decimal X { get; init; }
    public required decimal Y { get; init; }
}

/// <summary>
/// Geometric shape definition for physical spaces.
/// Supports rectangle (2 points: top-left, bottom-right) and polygon (3+ points).
/// </summary>
public record SpaceGeometry
{
    public required string Type { get; init; } // "rectangle" or "polygon"
    public required List<Coordinate> Coordinates { get; init; }

    /// <summary>
    /// Validates geometry based on type and coordinate count.
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Type))
            return false;

        var normalizedType = Type.ToLowerInvariant();
        if (normalizedType != "rectangle" && normalizedType != "polygon")
            return false;

        if (Coordinates == null || Coordinates.Count == 0)
            return false;

        // Rectangle requires exactly 2 points (top-left, bottom-right)
        if (normalizedType == "rectangle" && Coordinates.Count != 2)
            return false;

        // Polygon requires at least 3 points
        if (normalizedType == "polygon" && Coordinates.Count < 3)
            return false;

        return true;
    }

    /// <summary>
    /// Returns the bounding box of the geometry.
    /// </summary>
    public (decimal MinX, decimal MinY, decimal MaxX, decimal MaxY) GetBoundingBox()
    {
        if (Coordinates == null || Coordinates.Count == 0)
            throw new InvalidOperationException("Cannot get bounding box of empty geometry");

        var minX = Coordinates.Min(c => c.X);
        var minY = Coordinates.Min(c => c.Y);
        var maxX = Coordinates.Max(c => c.X);
        var maxY = Coordinates.Max(c => c.Y);

        return (minX, minY, maxX, maxY);
    }
}

/// <summary>
/// Complete space information including metadata and geometry.
/// </summary>
public record SpaceInfo
{
    public required Guid Id { get; init; }
    public required Guid SiteId { get; init; }
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public required bool IsPhysical { get; init; }
    public SpaceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public Guid? GroupId { get; init; }
    public int Capacity { get; init; } = 1;
    /// <summary>
    /// Values for the custom fields defined on the space resource type, keyed by field key.
    /// A space is an ordinary resource, so it carries the same descriptive properties any other
    /// type can — including lists, whose rows hang off the space itself.
    /// </summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>
    /// Projects a placeable resource onto the published space shape. A space is a resource whose
    /// type declares geometry, so this is a rename of fields rather than a lookup: SiteId is the
    /// home site, and the caller has already scoped its read to resources that have one.
    /// </summary>
    public static SpaceInfo FromResource(ResourceInfo resource) => new()
    {
        Id = resource.Id,
        SiteId = resource.HomeSiteId!.Value,
        Name = resource.Name,
        Code = resource.Code,
        Description = resource.Description,
        IsPhysical = resource.IsPhysical,
        Geometry = resource.Geometry,
        Properties = resource.Properties,
        GroupId = resource.GroupId,
        Capacity = resource.Capacity,
        CustomFields = resource.CustomFields,
        CreatedAt = resource.CreatedAt,
        UpdatedAt = resource.UpdatedAt,
    };
}

/// <summary>
/// Shared interface for space requests that carry an optional Geometry field.
/// Used by the shared validator base to avoid duplicating the geometry rule.
/// </summary>
public interface ISpaceGeometryRequest
{
    SpaceGeometry? Geometry { get; }
}

/// <summary>
/// Request to create a new space.
/// </summary>
public record CreateSpaceRequest : ISpaceGeometryRequest
{
    public required string Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public required bool IsPhysical { get; init; }
    public SpaceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public int Capacity { get; init; } = 1;
    /// <summary>
    /// Values for the space type's custom fields. Present here so a space can be created with
    /// them, which is what foundation#110 was waiting on — before this the only endpoint that
    /// creates a space had nowhere to put them.
    /// </summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }
}

/// <summary>
/// Request to update an existing space.
/// All fields are optional (partial update).
/// </summary>
public record UpdateSpaceRequest : ISpaceGeometryRequest
{
    public string? Name { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public SpaceGeometry? Geometry { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public int? Capacity { get; init; }
    /// <summary>
    /// The complete value document, as on any resource: it replaces what the space holds, so an
    /// omitted field is an emptied field. Null leaves the values untouched.
    /// </summary>
    public Dictionary<string, JsonElement>? CustomFields { get; init; }
}

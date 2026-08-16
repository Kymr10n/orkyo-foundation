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
/// Geometric shape of a placeable resource — anything whose type declares geometry, not spaces
/// alone. Supports rectangle (2 points: top-left, bottom-right) and polygon (3+ points).
/// </summary>
public record ResourceGeometry
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

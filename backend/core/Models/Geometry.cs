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
/// alone. Supports rectangle (2 points: top-left, bottom-right), polygon (3+ points) and circle
/// (2 points: centre, then any point on the rim — the radius is the distance between them).
/// </summary>
public record ResourceGeometry
{
    public required string Type { get; init; } // "rectangle", "polygon" or "circle"
    public required List<Coordinate> Coordinates { get; init; }

    /// <summary>
    /// Validates geometry based on type and coordinate count.
    /// </summary>
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(Type))
            return false;

        var normalizedType = Type.ToLowerInvariant();
        if (normalizedType is not (RectangleType or PolygonType or CircleType))
            return false;

        if (Coordinates == null || Coordinates.Count == 0)
            return false;

        // Rectangle is two opposite corners; a circle is its centre and a point on the rim.
        if (normalizedType is RectangleType or CircleType && Coordinates.Count != 2)
            return false;

        // Polygon requires at least 3 points
        if (normalizedType == PolygonType && Coordinates.Count < 3)
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

        // A circle's stored points are its centre and one rim point, so the extent of that pair is
        // a quadrant of the real box rather than the box itself. Every other shape is described by
        // points that sit on its outline, where the extent is the answer.
        if (Type.ToLowerInvariant() == CircleType && Coordinates.Count == 2)
        {
            var centre = Coordinates[0];
            var radius = Radius();
            return (centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);
        }

        var minX = Coordinates.Min(c => c.X);
        var minY = Coordinates.Min(c => c.Y);
        var maxX = Coordinates.Max(c => c.X);
        var maxY = Coordinates.Max(c => c.Y);

        return (minX, minY, maxX, maxY);
    }

    private const string RectangleType = "rectangle";
    private const string PolygonType = "polygon";
    private const string CircleType = "circle";

    /// <summary>Distance from the centre to the stored rim point. Circles only.</summary>
    private decimal Radius()
    {
        var dx = (double)(Coordinates[1].X - Coordinates[0].X);
        var dy = (double)(Coordinates[1].Y - Coordinates[0].Y);
        return (decimal)Math.Sqrt((dx * dx) + (dy * dy));
    }
}

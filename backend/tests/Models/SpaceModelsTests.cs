using Api.Models;

namespace Api.Tests.Models;

/// <summary>
/// Tests for resource geometry validity and bounding boxes.
/// </summary>
public class SpaceModelsTests
{
    [Theory]
    [InlineData("rectangle", 2, true)]
    [InlineData("polygon", 3, true)]
    [InlineData("polygon", 5, true)]
    [InlineData("rectangle", 1, false)]
    [InlineData("rectangle", 3, false)]
    [InlineData("polygon", 2, false)]
    // A circle is its centre and one rim point — never one, never three.
    [InlineData("circle", 2, true)]
    [InlineData("circle", 1, false)]
    [InlineData("circle", 3, false)]
    public void SpaceGeometry_ValidateCoordinateCount(string type, int coordinateCount, bool expectedValid)
    {
        var geometry = new ResourceGeometry
        {
            Type = type,
            Coordinates = Enumerable.Range(0, coordinateCount)
                .Select(i => new Coordinate { X = i * 10, Y = i * 10 })
                .ToList()
        };

        var isValid = geometry.IsValid();

        Assert.Equal(expectedValid, isValid);
    }

    [Theory]
    [InlineData("rectangle", true)]
    [InlineData("polygon", true)]
    [InlineData("circle", true)]
    [InlineData("RECTANGLE", true)] // Case insensitive
    [InlineData("CIRCLE", true)]
    [InlineData("line", false)]
    [InlineData("", false)]
    public void SpaceGeometry_ValidateType(string type, bool expectedValid)
    {
        var geometry = new ResourceGeometry
        {
            Type = type,
            Coordinates = type.ToLower() is "rectangle" or "circle"
                ? new List<Coordinate> { new() { X = 0, Y = 0 }, new() { X = 100, Y = 100 } }
                : new List<Coordinate> { new() { X = 0, Y = 0 }, new() { X = 100, Y = 0 }, new() { X = 100, Y = 100 } }
        };

        var isValid = geometry.IsValid();

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void SpaceGeometry_GetBoundingBox_ForCircle_SpansTheWholeCircleNotTheStoredPoints()
    {
        // The stored pair is the centre and one rim point, so their extent is a quadrant of the
        // real box. Taking Min/Max over the coordinates — right for every other shape, whose
        // points sit on the outline — would report a box a quarter of the size.
        var geometry = new ResourceGeometry
        {
            Type = "circle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 100, Y = 100 },  // centre
                new() { X = 130, Y = 140 },  // rim: dx 30, dy 40 -> r 50
            }
        };

        var bounds = geometry.GetBoundingBox();

        Assert.Equal(50, bounds.MinX);
        Assert.Equal(50, bounds.MinY);
        Assert.Equal(150, bounds.MaxX);
        Assert.Equal(150, bounds.MaxY);
    }

    [Fact]
    public void SpaceGeometry_GetBoundingBox_ReturnsCorrectBounds()
    {
        var geometry = new ResourceGeometry
        {
            Type = "polygon",
            Coordinates = new List<Coordinate>
            {
                new() { X = 10, Y = 20 },
                new() { X = 50, Y = 5 },
                new() { X = 100, Y = 80 },
                new() { X = 30, Y = 90 }
            }
        };

        var bounds = geometry.GetBoundingBox();

        Assert.Equal(10, bounds.MinX);
        Assert.Equal(5, bounds.MinY);
        Assert.Equal(100, bounds.MaxX);
        Assert.Equal(90, bounds.MaxY);
    }
}

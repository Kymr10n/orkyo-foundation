using Api.Models;
using Api.Validators;
using FluentValidation;

namespace Orkyo.Foundation.Tests.Models;

public class SpaceGeometryResizeTests
{
    private readonly IValidator<UpdateSpaceRequest> _updateValidator = new UpdateSpaceRequestValidator();

    #region Rectangle Resize Tests

    [Fact]
    public void RectangleGeometry_ResizeByMovingCorner_RemainsValid()
    {
        var originalGeometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 100, Y = 100 },
                new() { X = 300, Y = 300 }
            }
        };

        var resizedGeometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 100, Y = 100 },
                new() { X = 400, Y = 400 }
            }
        };

        Assert.True(originalGeometry.IsValid());
        Assert.True(resizedGeometry.IsValid());
        Assert.Equal(2, resizedGeometry.Coordinates.Count);
    }

    [Fact]
    public void RectangleGeometry_ResizeToZeroArea_IsValid()
    {
        var geometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 100, Y = 100 },
                new() { X = 100, Y = 100 }
            }
        };

        Assert.True(geometry.IsValid());
    }

    [Fact]
    public void RectangleGeometry_ResizeToNegativeCoordinates_IsValid()
    {
        var geometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = -50, Y = -50 },
                new() { X = 100, Y = 100 }
            }
        };

        Assert.True(geometry.IsValid());
    }

    [Fact]
    public void RectangleGeometry_ResizeWithInvertedCorners_IsValid()
    {
        var geometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 300, Y = 300 },
                new() { X = 100, Y = 100 }
            }
        };

        Assert.True(geometry.IsValid());
        var bounds = geometry.GetBoundingBox();
        Assert.Equal(100, bounds.MinX);
        Assert.Equal(100, bounds.MinY);
        Assert.Equal(300, bounds.MaxX);
        Assert.Equal(300, bounds.MaxY);
    }

    #endregion

    #region Polygon Resize Tests

    [Fact]
    public void PolygonGeometry_ResizeByMovingVertex_RemainsValid()
    {
        var originalGeometry = new SpaceGeometry
        {
            Type = "polygon",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 100, Y = 0 },
                new() { X = 50, Y = 100 }
            }
        };

        var resizedGeometry = new SpaceGeometry
        {
            Type = "polygon",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 150, Y = 0 },
                new() { X = 50, Y = 100 }
            }
        };

        Assert.True(originalGeometry.IsValid());
        Assert.True(resizedGeometry.IsValid());
    }

    [Fact]
    public void PolygonGeometry_ResizeComplexShape_MaintainsVertexCount()
    {
        var originalGeometry = new SpaceGeometry
        {
            Type = "polygon",
            Coordinates = new List<Coordinate>
            {
                new() { X = 50, Y = 0 },
                new() { X = 100, Y = 38 },
                new() { X = 81, Y = 95 },
                new() { X = 19, Y = 95 },
                new() { X = 0, Y = 38 }
            }
        };

        var resizedGeometry = new SpaceGeometry
        {
            Type = originalGeometry.Type,
            Coordinates = originalGeometry.Coordinates
                .Select((coord, index) => index == 1
                    ? new Coordinate { X = coord.X + 20, Y = coord.Y + 10 }
                    : coord)
                .ToList()
        };

        Assert.Equal(5, resizedGeometry.Coordinates.Count);
        Assert.True(resizedGeometry.IsValid());
    }

    [Fact]
    public void PolygonGeometry_ResizeToCollinearPoints_StillValid()
    {
        var geometry = new SpaceGeometry
        {
            Type = "polygon",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 100, Y = 0 },
                new() { X = 200, Y = 0 }
            }
        };

        Assert.True(geometry.IsValid());
    }

    #endregion

    #region Bounding Box After Resize Tests

    [Fact]
    public void GetBoundingBox_AfterEnlarging_ReturnsCorrectBounds()
    {
        var original = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 10, Y = 10 },
                new() { X = 20, Y = 20 }
            }
        };

        var enlarged = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 10, Y = 10 },
                new() { X = 30, Y = 30 }
            }
        };

        var originalBounds = original.GetBoundingBox();
        var enlargedBounds = enlarged.GetBoundingBox();

        Assert.Equal(10, originalBounds.MaxX - originalBounds.MinX);
        Assert.Equal(10, originalBounds.MaxY - originalBounds.MinY);
        Assert.Equal(20, enlargedBounds.MaxX - enlargedBounds.MinX);
        Assert.Equal(20, enlargedBounds.MaxY - enlargedBounds.MinY);
    }

    [Fact]
    public void GetBoundingBox_AfterShrinking_ReturnsCorrectBounds()
    {
        var original = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 100, Y = 100 }
            }
        };

        var shrunk = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 50, Y = 50 }
            }
        };

        var originalBounds = original.GetBoundingBox();
        var shrunkBounds = shrunk.GetBoundingBox();

        Assert.Equal(100, originalBounds.MaxX - originalBounds.MinX);
        Assert.Equal(50, shrunkBounds.MaxX - shrunkBounds.MinX);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void Geometry_ResizeWithVeryLargeCoordinates_IsValid()
    {
        var geometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 0, Y = 0 },
                new() { X = 10000, Y = 10000 }
            }
        };

        Assert.True(geometry.IsValid());
    }

    [Fact]
    public void Geometry_ResizeWithFloatingPointPrecision_HandlesCorrectly()
    {
        var geometry = new SpaceGeometry
        {
            Type = "rectangle",
            Coordinates = new List<Coordinate>
            {
                new() { X = 100.5M, Y = 100.5M },
                new() { X = 200.75M, Y = 200.75M }
            }
        };

        Assert.True(geometry.IsValid());
        var bounds = geometry.GetBoundingBox();
        Assert.Equal(100.25M, bounds.MaxX - bounds.MinX);
        Assert.Equal(100.25M, bounds.MaxY - bounds.MinY);
    }

    #endregion

    #region UpdateSpaceRequest With Geometry Tests

    [Fact]
    public void UpdateSpaceRequest_WithResizedGeometry_IsValid()
    {
        var request = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 50, Y = 50 },
                    new() { X = 150, Y = 150 }
                }
            }
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpaceRequest_WithOnlyGeometryChange_LeavesOtherFieldsUnchanged()
    {
        var request = new UpdateSpaceRequest
        {
            Name = null,
            Description = null,
            Geometry = new SpaceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 200, Y = 200 }
                }
            }
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
        Assert.Null(request.Name);
        Assert.Null(request.Description);
        Assert.NotNull(request.Geometry);
    }

    #endregion
}

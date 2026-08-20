using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Models;

/// <summary>
/// Tests for Space model validation and geometry parsing.
/// </summary>
public class SpaceModelsTests
{
    private readonly IValidator<CreateResourceRequest> _createValidator = new CreateResourceRequestValidator();
    private readonly IValidator<UpdateResourceRequest> _updateValidator = new UpdateResourceRequestValidator();

    #region Coordinate Tests

    [Fact]
    public void Coordinate_ValidValues_CreatesSuccessfully()
    {
        var coord = new Coordinate { X = 100, Y = 200 };

        Assert.Equal(100, coord.X);
        Assert.Equal(200, coord.Y);
    }

    [Fact]
    public void Coordinate_NegativeValues_AreAllowed()
    {
        var coord = new Coordinate { X = -50, Y = -100 };

        Assert.Equal(-50, coord.X);
        Assert.Equal(-100, coord.Y);
    }

    #endregion

    #region ResourceGeometry Tests

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

    #endregion

    #region CreateResourceRequest Validation Tests

    [Fact]
    public void CreateResourceRequest_ValidVirtualSpace_PassesValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = "Virtual Space",
            Code = "V-01",
            IsPhysical = false,
            Geometry = null
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateResourceRequest_ValidPhysicalSpace_PassesValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = "Physical Space",
            IsPhysical = true,
            Geometry = new ResourceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 100 }
                }
            }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateResourceRequest_PhysicalSpaceWithoutGeometry_FailsValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = "Invalid Physical Space",
            IsPhysical = true,
            Geometry = null
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("geometry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateResourceRequest_EmptyName_FailsValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = "",
            IsPhysical = false
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateResourceRequest_NullName_FailsValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = null!,
            IsPhysical = false
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateResourceRequest_InvalidGeometry_FailsValidation()
    {
        var request = new CreateResourceRequest
        {
            ResourceTypeKey = ResourceTypeKeys.Space,
            AllocationMode = AllocationModes.Exclusive,
            Name = "Space",
            IsPhysical = true,
            Geometry = new ResourceGeometry
            {
                Type = "rectangle",
                Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } } // Only 1 point
            }
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("coordinate", StringComparison.OrdinalIgnoreCase)
            || e.ErrorMessage.Contains("geometry", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region UpdateResourceRequest Validation Tests

    [Fact]
    public void UpdateResourceRequest_PartialUpdate_IsValid()
    {
        var request = new UpdateResourceRequest
        {
            Name = "Updated Name"
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateResourceRequest_UpdateGeometryOnly_IsValid()
    {
        var request = new UpdateResourceRequest
        {
            Geometry = new ResourceGeometry
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
    }

    [Fact]
    public void UpdateResourceRequest_InvalidGeometry_FailsValidation()
    {
        var request = new UpdateResourceRequest
        {
            Geometry = new ResourceGeometry
            {
                Type = "invalid",
                Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } }
            }
        };

        var result = _updateValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    #endregion

}

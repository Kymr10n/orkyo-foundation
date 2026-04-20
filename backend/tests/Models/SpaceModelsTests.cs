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
    private readonly IValidator<CreateSpaceRequest> _createValidator = new CreateSpaceRequestValidator();
    private readonly IValidator<UpdateSpaceRequest> _updateValidator = new UpdateSpaceRequestValidator();

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

    #region SpaceGeometry Tests

    [Theory]
    [InlineData("rectangle", 2, true)]
    [InlineData("polygon", 3, true)]
    [InlineData("polygon", 5, true)]
    [InlineData("rectangle", 1, false)]
    [InlineData("rectangle", 3, false)]
    [InlineData("polygon", 2, false)]
    public void SpaceGeometry_ValidateCoordinateCount(string type, int coordinateCount, bool expectedValid)
    {
        var geometry = new SpaceGeometry
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
    [InlineData("RECTANGLE", true)] // Case insensitive
    [InlineData("circle", false)]
    [InlineData("line", false)]
    [InlineData("", false)]
    public void SpaceGeometry_ValidateType(string type, bool expectedValid)
    {
        var geometry = new SpaceGeometry
        {
            Type = type,
            Coordinates = type.ToLower() == "rectangle"
                ? new List<Coordinate> { new() { X = 0, Y = 0 }, new() { X = 100, Y = 100 } }
                : new List<Coordinate> { new() { X = 0, Y = 0 }, new() { X = 100, Y = 0 }, new() { X = 100, Y = 100 } }
        };

        var isValid = geometry.IsValid();

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void SpaceGeometry_GetBoundingBox_ReturnsCorrectBounds()
    {
        var geometry = new SpaceGeometry
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

    #region CreateSpaceRequest Validation Tests

    [Fact]
    public void CreateSpaceRequest_ValidVirtualSpace_PassesValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Virtual Space",
            Code = "V-01",
            IsPhysical = false,
            Geometry = null
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateSpaceRequest_ValidPhysicalSpace_PassesValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Physical Space",
            IsPhysical = true,
            Geometry = new SpaceGeometry
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
    public void CreateSpaceRequest_PhysicalSpaceWithoutGeometry_FailsValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Invalid Physical Space",
            IsPhysical = true,
            Geometry = null
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("geometry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateSpaceRequest_EmptyName_FailsValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = "",
            IsPhysical = false
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateSpaceRequest_NullName_FailsValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = null!,
            IsPhysical = false
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateSpaceRequest_InvalidGeometry_FailsValidation()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Space",
            IsPhysical = true,
            Geometry = new SpaceGeometry
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

    #region UpdateSpaceRequest Validation Tests

    [Fact]
    public void UpdateSpaceRequest_PartialUpdate_IsValid()
    {
        var request = new UpdateSpaceRequest
        {
            Name = "Updated Name"
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpaceRequest_UpdateGeometryOnly_IsValid()
    {
        var request = new UpdateSpaceRequest
        {
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
    }

    [Fact]
    public void UpdateSpaceRequest_InvalidGeometry_FailsValidation()
    {
        var request = new UpdateSpaceRequest
        {
            Geometry = new SpaceGeometry
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

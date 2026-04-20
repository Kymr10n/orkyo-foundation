using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class SpaceValidatorTests
{
    private readonly IValidator<CreateSpaceRequest> _createValidator = new CreateSpaceRequestValidator();
    private readonly IValidator<UpdateSpaceRequest> _updateValidator = new UpdateSpaceRequestValidator();

    private static SpaceGeometry ValidRectangle => new()
    {
        Type = "rectangle",
        Coordinates = new List<Coordinate>
        {
            new() { X = 0, Y = 0 },
            new() { X = 100, Y = 100 }
        }
    };

    private static SpaceGeometry ValidPolygon => new()
    {
        Type = "polygon",
        Coordinates = new List<Coordinate>
        {
            new() { X = 0, Y = 0 },
            new() { X = 100, Y = 0 },
            new() { X = 50, Y = 100 }
        }
    };

    private static SpaceGeometry InvalidGeometry => new()
    {
        Type = "rectangle",
        Coordinates = new List<Coordinate> { new() { X = 0, Y = 0 } } // needs 2
    };

    #region CreateSpaceRequest

    [Fact]
    public void Create_ValidPhysicalSpace_Passes()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Room A",
            IsPhysical = true,
            Geometry = ValidRectangle
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_ValidVirtualSpace_Passes()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Virtual Room",
            IsPhysical = false
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_ValidPolygonSpace_Passes()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Open Area",
            IsPhysical = true,
            Geometry = ValidPolygon
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_EmptyName_Fails(string? name)
    {
        var request = new CreateSpaceRequest
        {
            Name = name!,
            IsPhysical = false
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var request = new CreateSpaceRequest
        {
            Name = new string('x', 201),
            IsPhysical = false
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Create_PhysicalWithoutGeometry_Fails()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Room A",
            IsPhysical = true,
            Geometry = null
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Physical spaces must have geometry"));
    }

    [Fact]
    public void Create_VirtualWithoutGeometry_Passes()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Virtual",
            IsPhysical = false,
            Geometry = null
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_InvalidGeometry_Fails()
    {
        var request = new CreateSpaceRequest
        {
            Name = "Room A",
            IsPhysical = true,
            Geometry = InvalidGeometry
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Invalid geometry"));
    }

    [Fact]
    public void Create_VirtualWithGeometry_ValidatesGeometry()
    {
        // Virtual spaces can optionally have geometry, but if provided it must be valid
        var request = new CreateSpaceRequest
        {
            Name = "Virtual",
            IsPhysical = false,
            Geometry = InvalidGeometry
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
    }

    #endregion

    #region UpdateSpaceRequest

    [Fact]
    public void Update_EmptyRequest_Passes()
    {
        var result = _updateValidator.Validate(new UpdateSpaceRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_ValidGeometry_Passes()
    {
        var request = new UpdateSpaceRequest { Geometry = ValidRectangle };
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_ValidPolygonGeometry_Passes()
    {
        var request = new UpdateSpaceRequest { Geometry = ValidPolygon };
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_InvalidGeometry_Fails()
    {
        var request = new UpdateSpaceRequest { Geometry = InvalidGeometry };
        var result = _updateValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Invalid geometry"));
    }

    [Fact]
    public void Update_NoGeometry_Passes()
    {
        var request = new UpdateSpaceRequest { Name = "Updated Room" };
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    #endregion
}

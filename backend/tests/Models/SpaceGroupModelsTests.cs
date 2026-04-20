using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Models;

/// <summary>
/// Tests for SpaceGroup model validation via FluentValidation validators.
/// </summary>
public class SpaceGroupModelsTests
{
    private readonly IValidator<CreateSpaceGroupRequest> _createValidator = new CreateSpaceGroupRequestValidator();
    private readonly IValidator<UpdateSpaceGroupRequest> _updateValidator = new UpdateSpaceGroupRequestValidator();

    #region CreateSpaceGroupRequest Validation Tests

    [Fact]
    public void CreateSpaceGroupRequest_ValidData_PassesValidation()
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = "Meeting Rooms",
            Description = "All meeting rooms",
            Color = "#3b82f6",
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateSpaceGroupRequest_MinimalData_PassesValidation()
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test Group",
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateSpaceGroupRequest_EmptyName_FailsValidation()
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = "",
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateSpaceGroupRequest_NullName_FailsValidation()
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = null!,
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#3b82f6")]
    [InlineData("#ef4444")]
    [InlineData("#10b981")]
    public void CreateSpaceGroupRequest_ValidHexColors_PassValidation(string color)
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test",
            Color = color,
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("rgb(255,0,0)")]
    [InlineData("#FFF")] // Too short
    [InlineData("#FFFFFFF")] // Too long
    [InlineData("FFFFFF")] // Missing #
    [InlineData("#GGGGGG")] // Invalid hex
    public void CreateSpaceGroupRequest_InvalidHexColors_FailValidation(string color)
    {
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test",
            Color = color,
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("color", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateSpaceGroupRequest_DescriptionTooLong_FailsValidation()
    {
        var longDescription = new string('x', 1001);
        var request = new CreateSpaceGroupRequest
        {
            Name = "Test",
            Description = longDescription,
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Description");
    }

    [Fact]
    public void CreateSpaceGroupRequest_NameTooLong_FailsValidation()
    {
        var longName = new string('x', 256);
        var request = new CreateSpaceGroupRequest
        {
            Name = longName,
            DisplayOrder = 0
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    #endregion

    #region UpdateSpaceGroupRequest Validation Tests

    [Fact]
    public void UpdateSpaceGroupRequest_ValidData_PassesValidation()
    {
        var request = new UpdateSpaceGroupRequest
        {
            Name = "Updated Name",
            Description = "Updated description",
            Color = "#ef4444",
            DisplayOrder = 5
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpaceGroupRequest_PartialUpdate_PassesValidation()
    {
        var request = new UpdateSpaceGroupRequest
        {
            Name = "Updated Name"
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpaceGroupRequest_EmptyRequest_PassesValidation()
    {
        var request = new UpdateSpaceGroupRequest();

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateSpaceGroupRequest_EmptyName_FailsValidation()
    {
        var request = new UpdateSpaceGroupRequest
        {
            Name = ""
        };

        var result = _updateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void UpdateSpaceGroupRequest_InvalidColor_FailsValidation()
    {
        var request = new UpdateSpaceGroupRequest
        {
            Color = "invalid-color"
        };

        var result = _updateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("color", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

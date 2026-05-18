using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Models;

/// <summary>
/// Tests for Criterion model validation via FluentValidation validators.
/// </summary>
public class CriterionModelsTests
{
    private readonly IValidator<CreateCriterionRequest> _createValidator = new CreateCriterionRequestValidator();
    private readonly IValidator<UpdateCriterionRequest> _updateValidator = new UpdateCriterionRequestValidator();

    #region CreateCriterionRequest Validation

    [Fact]
    public void CreateCriterionRequest_WithValidName_PassesValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "valid_criterion_name",
            Description = "Test criterion",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCriterionRequest_WithEmptyName_FailsValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "",
            DataType = CriterionDataType.Boolean
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void CreateCriterionRequest_WithInvalidCharactersInName_FailsValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "invalid name with spaces",
            DataType = CriterionDataType.Boolean
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("letter") || e.ErrorMessage.Contains("Name"));
    }

    [Theory]
    [InlineData("special@char")]
    [InlineData("has spaces")]
    [InlineData("special!")]
    [InlineData("test.criterion")]
    public void CreateCriterionRequest_WithSpecialCharacters_FailsValidation(string name)
    {
        var request = new CreateCriterionRequest
        {
            Name = name,
            DataType = CriterionDataType.Boolean
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("letter"));
    }

    [Theory]
    [InlineData("valid_name")]
    [InlineData("valid-name")]
    [InlineData("validName123")]
    [InlineData("UPPERCASE_NAME")]
    [InlineData("mixed_CASE-123")]
    public void CreateCriterionRequest_WithValidNameFormats_PassesValidation(string name)
    {
        var request = new CreateCriterionRequest
        {
            Name = name,
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = new List<string> { "space" }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCriterionRequest_EnumTypeWithEmptyValues_FailsValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "test_enum",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string>()
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("enum value"));
    }

    [Fact]
    public void CreateCriterionRequest_EnumTypeWithNullValues_FailsValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "test_enum",
            DataType = CriterionDataType.Enum,
            EnumValues = null
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("enum value"));
    }

    [Fact]
    public void CreateCriterionRequest_EnumTypeWithValidValues_PassesValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "test_enum",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "S", "M", "L", "XL" },
            ResourceTypeKeys = new List<string> { "space" }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCriterionRequest_BooleanType_AllowsNullEnumValues()
    {
        var request = new CreateCriterionRequest
        {
            Name = "test_boolean",
            DataType = CriterionDataType.Boolean,
            EnumValues = null,
            ResourceTypeKeys = new List<string> { "space" }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateCriterionRequest_NumberTypeWithUnit_PassesValidation()
    {
        var request = new CreateCriterionRequest
        {
            Name = "test_number",
            DataType = CriterionDataType.Number,
            Unit = "kg",
            ResourceTypeKeys = new List<string> { "space" }
        };

        var result = _createValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    #endregion

    #region UpdateCriterionRequest Validation

    [Fact]
    public void UpdateCriterionRequest_WithValidData_PassesValidation()
    {
        var request = new UpdateCriterionRequest
        {
            Description = "Updated description"
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateCriterionRequest_WithAllFieldsNull_PassesValidation()
    {
        var request = new UpdateCriterionRequest
        {
            Description = null,
            EnumValues = null,
            Unit = null
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateCriterionRequest_WithEmptyEnumValues_FailsValidation()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string>()
        };

        var result = _updateValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("empty"));
    }

    [Fact]
    public void UpdateCriterionRequest_WithValidEnumValues_PassesValidation()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "Small", "Medium", "Large" }
        };

        var result = _updateValidator.Validate(request);

        Assert.True(result.IsValid);
    }

    #endregion

    #region CriterionDataType Enum

    [Fact]
    public void CriterionDataType_HasExpectedValues()
    {
        Assert.Equal("Boolean", CriterionDataType.Boolean.ToString());
        Assert.Equal("Number", CriterionDataType.Number.ToString());
        Assert.Equal("String", CriterionDataType.String.ToString());
        Assert.Equal("Enum", CriterionDataType.Enum.ToString());
    }

    #endregion

    #region CriterionInfo

    [Fact]
    public void CriterionInfo_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var info = new CriterionInfo
        {
            Id = id,
            Name = "Weight",
            Description = "Load-bearing capacity in kg",
            DataType = CriterionDataType.Number,
            Unit = "kg",
            CreatedAt = now,
            UpdatedAt = now
        };

        info.Id.Should().Be(id);
        info.Name.Should().Be("Weight");
        info.DataType.Should().Be(CriterionDataType.Number);
        info.Unit.Should().Be("kg");
    }

    [Fact]
    public void CriterionInfo_EnumValues_StoredWhenProvided()
    {
        var info = new CriterionInfo
        {
            Id = Guid.NewGuid(),
            Name = "Shift Model",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "2-shift", "3-shift", "4-shift" }
        };

        info.EnumValues.Should().HaveCount(3);
        info.Unit.Should().BeNull();
    }

    #endregion
}

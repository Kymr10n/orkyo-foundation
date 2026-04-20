using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class CriterionValidatorTests
{
    private readonly IValidator<CreateCriterionRequest> _createValidator = new CreateCriterionRequestValidator();
    private readonly IValidator<UpdateCriterionRequest> _updateValidator = new UpdateCriterionRequestValidator();

    #region CreateCriterionRequest

    [Fact]
    public void Create_ValidNumberCriterion_Passes()
    {
        var request = new CreateCriterionRequest
        {
            Name = "Temperature",
            DataType = CriterionDataType.Number,
            Unit = "°C"
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_ValidEnumCriterion_Passes()
    {
        var request = new CreateCriterionRequest
        {
            Name = "FloorType",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "Carpet", "Wood", "Tile" }
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_ValidBooleanCriterion_Passes()
    {
        var request = new CreateCriterionRequest
        {
            Name = "HasWifi",
            DataType = CriterionDataType.Boolean
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_EmptyName_Fails(string? name)
    {
        var request = new CreateCriterionRequest
        {
            Name = name!,
            DataType = CriterionDataType.Number
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var request = new CreateCriterionRequest
        {
            Name = new string('a', DomainLimits.CriterionNameMaxLength + 1),
            DataType = CriterionDataType.Number
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("validName")]
    [InlineData("Name_with_underscores")]
    [InlineData("Name-with-hyphens")]
    [InlineData("A123")]
    public void Create_ValidNameFormats_Pass(string name)
    {
        var request = new CreateCriterionRequest
        {
            Name = name,
            DataType = CriterionDataType.Number
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1StartWithNumber")]
    [InlineData("_StartWithUnderscore")]
    [InlineData("-StartWithHyphen")]
    [InlineData("has spaces")]
    [InlineData("has.dots")]
    public void Create_InvalidNameFormats_Fail(string name)
    {
        var request = new CreateCriterionRequest
        {
            Name = name,
            DataType = CriterionDataType.Number
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must start with a letter"));
    }

    [Fact]
    public void Create_EnumTypeWithoutValues_Fails()
    {
        var request = new CreateCriterionRequest
        {
            Name = "FloorType",
            DataType = CriterionDataType.Enum,
            EnumValues = null
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Enum type requires"));
    }

    [Fact]
    public void Create_EnumTypeWithEmptyList_Fails()
    {
        var request = new CreateCriterionRequest
        {
            Name = "FloorType",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string>()
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Create_EnumTypeWithEmptyStringValue_Fails()
    {
        var request = new CreateCriterionRequest
        {
            Name = "FloorType",
            DataType = CriterionDataType.Enum,
            EnumValues = new List<string> { "Wood", "" }
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Enum values cannot be empty"));
    }

    [Fact]
    public void Create_NonEnumType_IgnoresEnumValues()
    {
        var request = new CreateCriterionRequest
        {
            Name = "Temperature",
            DataType = CriterionDataType.Number,
            EnumValues = null // OK for non-enum
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_UnitTooLong_Fails()
    {
        var request = new CreateCriterionRequest
        {
            Name = "Temperature",
            DataType = CriterionDataType.Number,
            Unit = new string('x', DomainLimits.CriterionUnitMaxLength + 1)
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Unit");
    }

    [Fact]
    public void Create_UnitAtMaxLength_Passes()
    {
        var request = new CreateCriterionRequest
        {
            Name = "Temperature",
            DataType = CriterionDataType.Number,
            Unit = new string('x', DomainLimits.CriterionUnitMaxLength)
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    #endregion

    #region UpdateCriterionRequest

    [Fact]
    public void Update_EmptyRequest_Passes()
    {
        var result = _updateValidator.Validate(new UpdateCriterionRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_ValidEnumValues_Passes()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "A", "B", "C" }
        };
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_EmptyEnumValuesList_Fails()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string>()
        };
        var result = _updateValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Enum values cannot be empty when provided"));
    }

    [Fact]
    public void Update_DuplicateEnumValues_Fails()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "A", "B", "A" }
        };
        var result = _updateValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("unique"));
    }

    [Fact]
    public void Update_EnumValuesWithEmptyString_Fails()
    {
        var request = new UpdateCriterionRequest
        {
            EnumValues = new List<string> { "A", "" }
        };
        var result = _updateValidator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_UnitTooLong_Fails()
    {
        var request = new UpdateCriterionRequest
        {
            Unit = new string('x', DomainLimits.CriterionUnitMaxLength + 1)
        };
        var result = _updateValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Unit");
    }

    [Fact]
    public void Update_ValidUnit_Passes()
    {
        var request = new UpdateCriterionRequest { Unit = "kg" };
        var result = _updateValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    #endregion
}

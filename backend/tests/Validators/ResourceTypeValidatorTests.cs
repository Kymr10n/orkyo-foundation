using System.Text.Json;
using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class ResourceTypeValidatorTests
{
    private readonly IValidator<CreateResourceTypeRequest> _createType = new CreateResourceTypeRequestValidator();
    private readonly IValidator<UpdateResourceTypeRequest> _updateType = new UpdateResourceTypeRequestValidator();
    private readonly IValidator<CreateResourceTypeFieldRequest> _createField = new CreateResourceTypeFieldRequestValidator();
    private readonly IValidator<UpdateResourceTypeFieldRequest> _updateField = new UpdateResourceTypeFieldRequestValidator();

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    #region Resource type

    [Fact]
    public void CreateType_ValidRequest_Passes()
    {
        var result = _createType.Validate(new CreateResourceTypeRequest
        {
            Key = "car",
            DisplayName = "Car",
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Car")]
    [InlineData("2fast")]
    [InlineData("my-car")]
    [InlineData("my car")]
    public void CreateType_InvalidKey_Fails(string key)
    {
        var result = _createType.Validate(new CreateResourceTypeRequest
        {
            Key = key,
            DisplayName = "Car",
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateResourceTypeRequest.Key));
    }

    [Fact]
    public void CreateType_UnderscoresAndDigits_Pass()
    {
        var result = _createType.Validate(new CreateResourceTypeRequest
        {
            Key = "company_car_2",
            DisplayName = "Company car",
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateType_EmptyDisplayName_Fails()
    {
        var result = _createType.Validate(new CreateResourceTypeRequest { Key = "car", DisplayName = "" });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateType_AllNull_Passes()
    {
        Assert.True(_updateType.Validate(new UpdateResourceTypeRequest()).IsValid);
    }

    [Fact]
    public void UpdateType_BlankDisplayName_Fails()
    {
        Assert.False(_updateType.Validate(new UpdateResourceTypeRequest { DisplayName = "" }).IsValid);
    }

    #endregion

    #region Field definitions

    [Fact]
    public void CreateField_ValidNumberField_Passes()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "mileage",
            Label = "Mileage",
            DataType = ResourceFieldDataTypes.Number,
            Validation = Json("""{"min":0,"max":500000}"""),
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateField_UnknownDataType_Fails()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "price",
            Label = "Price",
            DataType = "money",
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateField_SelectWithoutOptions_Fails()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "fuel",
            Label = "Fuel",
            DataType = ResourceFieldDataTypes.Select,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateField_SelectWithEmptyValues_Fails()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "fuel",
            Label = "Fuel",
            DataType = ResourceFieldDataTypes.Select,
            Options = Json("""{"values":[]}"""),
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateField_SelectWithOptions_Passes()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "fuel",
            Label = "Fuel",
            DataType = ResourceFieldDataTypes.Select,
            Options = Json("""{"values":["petrol","diesel"]}"""),
        });

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("""{"min":"low"}""")]
    [InlineData("""{"regex":5}""")]
    [InlineData("""{"maxLength":0}""")]
    [InlineData("""{"unsupported":1}""")]
    [InlineData("""[1,2]""")]
    public void CreateField_MalformedValidation_Fails(string validation)
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "plate",
            Label = "Plate",
            DataType = ResourceFieldDataTypes.Text,
            Validation = Json(validation),
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void CreateField_NoValidation_Passes()
    {
        var result = _createField.Validate(new CreateResourceTypeFieldRequest
        {
            Key = "plate",
            Label = "Plate",
            DataType = ResourceFieldDataTypes.Text,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateField_AllNull_Passes()
    {
        Assert.True(_updateField.Validate(new UpdateResourceTypeFieldRequest()).IsValid);
    }

    [Fact]
    public void UpdateField_BlankLabel_Fails()
    {
        Assert.False(_updateField.Validate(new UpdateResourceTypeFieldRequest { Label = "" }).IsValid);
    }

    #endregion
}

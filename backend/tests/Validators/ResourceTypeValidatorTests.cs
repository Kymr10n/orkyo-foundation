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

}

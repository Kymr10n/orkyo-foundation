using Api.Constants;
using Api.Endpoints;
using Api.Validators;
using FluentValidation;

namespace Orkyo.Foundation.Tests.Validators;

public class SiteValidatorTests
{
    private readonly IValidator<CreateSiteRequest> _createValidator = new CreateSiteRequestValidator();
    private readonly IValidator<UpdateSiteRequest> _updateValidator = new UpdateSiteRequestValidator();

    #region CreateSiteRequest

    [Fact]
    public void Create_ValidData_Passes()
    {
        var result = _createValidator.Validate(new CreateSiteRequest("HQ", "Headquarters", "Main building", "123 Main St"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_MinimalData_Passes()
    {
        var result = _createValidator.Validate(new CreateSiteRequest("S1", "Site One", null, null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "Name")]
    [InlineData(null, "Name")]
    public void Create_EmptyName_Fails(string? name, string _)
    {
        var result = _createValidator.Validate(new CreateSiteRequest("HQ", name!, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("", "Code")]
    [InlineData(null, "Code")]
    public void Create_EmptyCode_Fails(string? code, string _)
    {
        var result = _createValidator.Validate(new CreateSiteRequest(code!, "Site", null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
    }

    [Fact]
    public void Create_CodeTooLong_Fails()
    {
        var longCode = new string('x', DomainLimits.SiteCodeMaxLength + 1);
        var result = _createValidator.Validate(new CreateSiteRequest(longCode, "Site", null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var longName = new string('x', DomainLimits.SiteNameMaxLength + 1);
        var result = _createValidator.Validate(new CreateSiteRequest("HQ", longName, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Create_MaxLengthCode_Passes()
    {
        var maxCode = new string('x', DomainLimits.SiteCodeMaxLength);
        var result = _createValidator.Validate(new CreateSiteRequest(maxCode, "Site", null, null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_MaxLengthName_Passes()
    {
        var maxName = new string('x', DomainLimits.SiteNameMaxLength);
        var result = _createValidator.Validate(new CreateSiteRequest("HQ", maxName, null, null));
        Assert.True(result.IsValid);
    }

    #endregion

    #region UpdateSiteRequest

    [Fact]
    public void Update_ValidData_Passes()
    {
        var result = _updateValidator.Validate(new UpdateSiteRequest("HQ", "Headquarters", "Updated", "456 Oak Ave"));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_EmptyCode_Fails()
    {
        var result = _updateValidator.Validate(new UpdateSiteRequest("", "Name", null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
    }

    [Fact]
    public void Update_EmptyName_Fails()
    {
        var result = _updateValidator.Validate(new UpdateSiteRequest("HQ", "", null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Update_CodeTooLong_Fails()
    {
        var longCode = new string('x', DomainLimits.SiteCodeMaxLength + 1);
        var result = _updateValidator.Validate(new UpdateSiteRequest(longCode, "Name", null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Code");
    }

    [Fact]
    public void Update_NameTooLong_Fails()
    {
        var longName = new string('x', DomainLimits.SiteNameMaxLength + 1);
        var result = _updateValidator.Validate(new UpdateSiteRequest("HQ", longName, null, null));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    #endregion
}

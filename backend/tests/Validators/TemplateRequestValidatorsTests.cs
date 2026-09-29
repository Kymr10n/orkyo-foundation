using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation.TestHelper;

namespace Api.Tests.Validators;

public class TemplateRequestValidatorsTests
{
    private readonly CreateTemplateRequestValidator _create = new();
    private readonly UpdateTemplateRequestValidator _update = new();
    private readonly CreateTemplateItemRequestValidator _item = new();

    [Fact]
    public void Create_EmptyName_Fails()
    {
        var result = _create.TestValidate(new CreateTemplateRequest { Name = "", EntityType = "request" });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Create_NameOverTheLimit_Fails()
    {
        var result = _create.TestValidate(new CreateTemplateRequest
        {
            Name = new string('A', DomainLimits.TemplateNameMaxLength + 1),
            EntityType = "request",
        });
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void Item_EmptyValue_Fails()
    {
        var result = _item.TestValidate(new CreateTemplateItemRequest { CriterionId = Guid.NewGuid(), Value = "" });
        result.ShouldHaveValidationErrorFor(x => x.Value);
    }

    [Fact]
    public void Create_TargetTypesAbsent_Passes()
    {
        var result = _create.TestValidate(new CreateTemplateRequest { Name = "Mill", EntityType = "request" });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_TargetTypesNamed_Passes()
    {
        var result = _create.TestValidate(new CreateTemplateRequest
        {
            Name = "Mill",
            EntityType = "request",
            TargetResourceTypeKeys = ["mill", "fixture"],
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Create_EmptyTargetTypeKey_Fails()
    {
        var result = _create.TestValidate(new CreateTemplateRequest
        {
            Name = "Mill",
            EntityType = "request",
            TargetResourceTypeKeys = ["mill", ""],
        });
        Assert.Contains(result.Errors, e => e.ErrorMessage == "TargetResourceTypeKeys must not contain empty keys");
    }

    [Fact]
    public void Update_EmptyListClearsAndPasses()
    {
        var result = _update.TestValidate(new UpdateTemplateRequest
        {
            Name = "Mill",
            EntityType = "request",
            TargetResourceTypeKeys = [],
        });
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_EmptyTargetTypeKey_Fails()
    {
        var result = _update.TestValidate(new UpdateTemplateRequest
        {
            Name = "Mill",
            EntityType = "request",
            TargetResourceTypeKeys = [" "],
        });
        Assert.Contains(result.Errors, e => e.ErrorMessage == "TargetResourceTypeKeys must not contain empty keys");
    }
}

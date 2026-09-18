using Api.Models;
using Api.Validators;
using FluentValidation.TestHelper;

namespace Api.Tests.Validators;

public class TemplateRequestValidatorsTests
{
    private readonly CreateTemplateRequestValidator _create = new();
    private readonly UpdateTemplateRequestValidator _update = new();

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

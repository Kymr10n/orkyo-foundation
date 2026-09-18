using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for templates. Entity type is checked through
/// <see cref="TemplateEntityTypes.IsKnown"/> so the vocabulary keeps its single owner, and the
/// length limits come from <see cref="DomainLimits"/> rather than repeated literals.
/// </summary>
public abstract class TemplateRequestValidatorBase<T> : AbstractValidator<T>
    where T : TemplateRequestBase
{
    protected TemplateRequestValidatorBase()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.TemplateNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(DomainLimits.TemplateDescriptionMaxLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.EntityType).NotEmpty()
            .Must(TemplateEntityTypes.IsKnown)
            .WithMessage($"EntityType must be one of: {string.Join(", ", TemplateEntityTypes.All)}");
        RuleFor(x => x.DurationValue!.Value).GreaterThan(0)
            .When(x => x.DurationValue.HasValue);
        RuleForEach(x => x.TargetResourceTypeKeys!).NotEmpty()
            .WithMessage("TargetResourceTypeKeys must not contain empty keys")
            .When(x => x.TargetResourceTypeKeys is not null);
    }
}

public class CreateTemplateRequestValidator : TemplateRequestValidatorBase<CreateTemplateRequest>
{
}

public class UpdateTemplateRequestValidator : TemplateRequestValidatorBase<UpdateTemplateRequest>
{
}

public class CreateTemplateItemRequestValidator : AbstractValidator<CreateTemplateItemRequest>
{
    public CreateTemplateItemRequestValidator()
    {
        RuleFor(x => x.CriterionId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty();
    }
}

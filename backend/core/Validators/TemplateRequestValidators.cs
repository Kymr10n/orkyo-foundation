using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for templates. Entity type is checked through
/// <see cref="TemplateEntityTypes.IsKnown"/> so the vocabulary keeps its single owner, and the
/// length limits come from <see cref="DomainLimits"/> rather than repeated literals.
/// </summary>
public class CreateTemplateRequestValidator : AbstractValidator<CreateTemplateRequest>
{
    public CreateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.TemplateNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(DomainLimits.TemplateDescriptionMaxLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.EntityType).NotEmpty()
            .Must(TemplateEntityTypes.IsKnown)
            .WithMessage($"EntityType must be one of: {string.Join(", ", TemplateEntityTypes.All)}");
        RuleFor(x => x.DurationValue!.Value).GreaterThan(0)
            .When(x => x.DurationValue.HasValue);
    }
}

public class UpdateTemplateRequestValidator : AbstractValidator<UpdateTemplateRequest>
{
    public UpdateTemplateRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.TemplateNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(DomainLimits.TemplateDescriptionMaxLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.EntityType).NotEmpty()
            .Must(TemplateEntityTypes.IsKnown)
            .WithMessage($"EntityType must be one of: {string.Join(", ", TemplateEntityTypes.All)}");
        RuleFor(x => x.DurationValue!.Value).GreaterThan(0)
            .When(x => x.DurationValue.HasValue);
    }
}

public class CreateTemplateItemRequestValidator : AbstractValidator<CreateTemplateItemRequest>
{
    public CreateTemplateItemRequestValidator()
    {
        RuleFor(x => x.CriterionId).NotEmpty();
        RuleFor(x => x.Value).NotEmpty();
    }
}

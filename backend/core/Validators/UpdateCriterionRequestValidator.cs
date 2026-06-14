using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpdateCriterionRequestValidator : AbstractValidator<UpdateCriterionRequest>
{
    public UpdateCriterionRequestValidator()
    {
        // Name is optional on update (null = no rename). When present it must satisfy the same
        // rules as create — see CreateCriterionRequestValidator.
        When(x => x.Name != null, () =>
            RuleFor(x => x.Name!)
                .NotEmpty()
                .MaximumLength(DomainLimits.CriterionNameMaxLength)
                .Matches(@"^[a-zA-Z][a-zA-Z0-9_-]*$")
                .WithMessage("Name must start with a letter and contain only letters, numbers, underscores, and hyphens"));

        When(x => x.Unit != null, () =>
            RuleFor(x => x.Unit!).MaximumLength(DomainLimits.CriterionUnitMaxLength));

        When(x => x.EnumValues != null, () =>
        {
            RuleFor(x => x.EnumValues!)
                .NotEmpty().WithMessage("Enum values cannot be empty when provided");
            RuleForEach(x => x.EnumValues).NotEmpty().WithMessage("Enum values cannot be empty");
            RuleFor(x => x.EnumValues!)
                .Must(e => e.Distinct().Count() == e.Count)
                .WithMessage("Enum values must be unique");
        });
    }
}

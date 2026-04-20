using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpdateCriterionRequestValidator : AbstractValidator<UpdateCriterionRequest>
{
    public UpdateCriterionRequestValidator()
    {
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

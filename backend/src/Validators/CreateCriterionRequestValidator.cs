using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateCriterionRequestValidator : AbstractValidator<CreateCriterionRequest>
{
    public CreateCriterionRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(DomainLimits.CriterionNameMaxLength)
            .Matches(@"^[a-zA-Z][a-zA-Z0-9_-]*$")
            .WithMessage("Name must start with a letter and contain only letters, numbers, underscores, and hyphens");

        When(x => x.DataType == CriterionDataType.Enum, () =>
        {
            RuleFor(x => x.EnumValues)
                .NotNull().WithMessage("Enum type requires at least one enum value")
                .NotEmpty().WithMessage("Enum type requires at least one enum value");
            RuleForEach(x => x.EnumValues).NotEmpty().WithMessage("Enum values cannot be empty");
        });

        When(x => x.Unit != null, () =>
            RuleFor(x => x.Unit!).MaximumLength(DomainLimits.CriterionUnitMaxLength));
    }
}

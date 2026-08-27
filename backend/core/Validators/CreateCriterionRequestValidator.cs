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
            .Matches(ValidationPatterns.Identifier)
            .WithMessage(ValidationPatterns.IdentifierMessage);

        When(x => x.DataType == CriterionDataType.Enum, () =>
        {
            RuleFor(x => x.EnumValues)
                .NotNull().WithMessage("Enum type requires at least one enum value")
                .NotEmpty().WithMessage("Enum type requires at least one enum value");
            RuleForEach(x => x.EnumValues).NotEmpty().WithMessage("Enum values cannot be empty");
        });

        When(x => x.Unit != null, () =>
            RuleFor(x => x.Unit!).MaximumLength(DomainLimits.CriterionUnitMaxLength));

        // Applicability: required, ≥1 entry, no duplicates or blanks. Whether each key names an
        // existing resource type is a database question (tenants define their own types), so it
        // is resolved when the applicability rows are written in CriteriaRepository.
        RuleFor(x => x.ResourceTypeKeys)
            .NotNull().WithMessage("At least one applicability value is required.")
            .Must(keys => keys is { Count: > 0 })
                .WithMessage("At least one applicability value is required.")
            .Must(keys => keys is null || keys.Distinct(StringComparer.Ordinal).Count() == keys.Count)
                .WithMessage("Duplicate applicability values are not allowed.");

        RuleForEach(x => x.ResourceTypeKeys!)
            .NotEmpty().WithMessage("Applicability values cannot be empty.");
    }
}

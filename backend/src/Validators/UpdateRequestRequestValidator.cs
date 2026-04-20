using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpdateRequestRequestValidator : AbstractValidator<UpdateRequestRequest>
{
    public UpdateRequestRequestValidator()
    {
        When(x => x.Name != null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(DomainLimits.RequestNameMaxLength));

        When(x => x.PlanningMode.HasValue, () =>
            RuleFor(x => x.PlanningMode!.Value).IsInEnum().WithMessage("Planning mode must be leaf, summary, or container"));

        When(x => x.EarliestStartTs.HasValue && x.LatestEndTs.HasValue, () =>
            RuleFor(x => x.LatestEndTs!.Value)
                .GreaterThan(x => x.EarliestStartTs!.Value)
                .WithMessage("Earliest start must be before latest end"));

        // Actual duration: both or neither
        RuleFor(x => x)
            .Must(x => x.ActualDurationValue.HasValue == x.ActualDurationUnit.HasValue)
            .WithMessage("Both actual_duration_value and actual_duration_unit must be provided together or both must be null");

        When(x => x.ActualDurationValue.HasValue, () =>
            RuleFor(x => x.ActualDurationValue!.Value).GreaterThan(0).WithMessage("Actual duration value must be positive"));

        When(x => x.MinimalDurationValue.HasValue, () =>
            RuleFor(x => x.MinimalDurationValue!.Value).GreaterThan(0).WithMessage("Minimal duration value must be positive"));
    }
}

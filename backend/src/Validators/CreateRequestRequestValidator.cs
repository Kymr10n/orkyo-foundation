using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateRequestRequestValidator : AbstractValidator<CreateRequestRequest>
{
    public CreateRequestRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.RequestNameMaxLength);
        RuleFor(x => x.MinimalDurationValue).GreaterThan(0).WithMessage("Minimal duration value must be positive");

        // Planning mode must be valid
        RuleFor(x => x.PlanningMode).IsInEnum().WithMessage("Planning mode must be leaf, summary, or container");

        // Non-leaf requests are structural only and cannot be scheduled directly
        When(x => x.PlanningMode == PlanningMode.Container, () =>
        {
            RuleFor(x => x.StartTs).Null().WithMessage("Container requests cannot have start_ts");
            RuleFor(x => x.EndTs).Null().WithMessage("Container requests cannot have end_ts");
            RuleFor(x => x.SpaceId).Null().WithMessage("Container requests cannot have a space_id");
        });

        // Summary requests: schedule is derived from children, not set directly
        When(x => x.PlanningMode == PlanningMode.Summary, () =>
        {
            RuleFor(x => x.StartTs).Null().WithMessage("Summary request dates are derived from children");
            RuleFor(x => x.EndTs).Null().WithMessage("Summary request dates are derived from children");
            RuleFor(x => x.SpaceId).Null().WithMessage("Summary requests cannot have a space_id");
        });

        // Both start/end must be provided together
        When(x => x.StartTs.HasValue || x.EndTs.HasValue, () =>
        {
            RuleFor(x => x.StartTs).NotNull().WithMessage("Both start_ts and end_ts must be provided together or both must be null");
            RuleFor(x => x.EndTs).NotNull().WithMessage("Both start_ts and end_ts must be provided together or both must be null");
        });
        When(x => x.StartTs.HasValue && x.EndTs.HasValue, () =>
            RuleFor(x => x.EndTs!.Value)
                .GreaterThan(x => x.StartTs!.Value)
                .WithMessage("End time must be after start time"));

        // Constraint dates order
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
    }
}

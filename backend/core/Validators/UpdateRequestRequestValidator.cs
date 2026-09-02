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

        When(x => x.Icon != null, () =>
            RuleFor(x => x.Icon!).MaximumLength(DomainLimits.RequestIconMaxLength));

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

        When(x => x.PredecessorLogic.HasValue, () =>
            RuleFor(x => x.PredecessorLogic!.Value).IsInEnum().WithMessage("Predecessor logic must be all, any, or k_of_n"));

        // k belongs to k_of_n and to nothing else. Both directions are checked so the pair can
        // never reach the database in a shape no reader knows how to interpret — the CHECK
        // constraint says the same thing, but a 400 explains it and a 23514 does not.
        When(x => x.PredecessorLogic == PredecessorLogic.KOfN, () =>
            RuleFor(x => x.PredecessorLogicK)
                .NotNull().WithMessage("Predecessor logic k_of_n needs a k")
                .GreaterThanOrEqualTo(1).WithMessage("k must be at least 1"));

        When(x => x.PredecessorLogic.HasValue && x.PredecessorLogic != PredecessorLogic.KOfN, () =>
            RuleFor(x => x.PredecessorLogicK)
                .Null().WithMessage("k applies only to k_of_n predecessor logic"));

        // The pair travels together, so a k on its own is refused rather than silently dropped:
        // the repository only writes k when the logic is present, so such a request would return
        // 200 with the old value still stored — a write that looks accepted and did nothing.
        When(x => x.PredecessorLogicK.HasValue && !x.PredecessorLogic.HasValue, () =>
            RuleFor(x => x.PredecessorLogic)
                .NotNull().WithMessage("Send predecessorLogic together with k"));
    }
}

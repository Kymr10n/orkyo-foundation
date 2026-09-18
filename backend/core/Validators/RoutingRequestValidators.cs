using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for routings. Whether a step's operation exists and can be scheduled is a
/// cross-entity rule and lives in <see cref="Services.RoutingService"/>.
/// </summary>
public class CreateRoutingRequestValidator : AbstractValidator<CreateRoutingRequest>
{
    public CreateRoutingRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.RoutingNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(DomainLimits.RoutingDescriptionMaxLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.Steps).NotEmpty().WithMessage("A routing needs at least one step");
        RuleFor(x => x.Steps).Must(RoutingSteps.AreNumberedInOrder)
            .WithMessage("Step numbers must run 1, 2, 3 … without gaps");
        RuleForEach(x => x.Steps).SetValidator(new RoutingStepRequestValidator());
    }
}

public class UpdateRoutingRequestValidator : AbstractValidator<UpdateRoutingRequest>
{
    public UpdateRoutingRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.RoutingNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(DomainLimits.RoutingDescriptionMaxLength)
            .When(x => x.Description is not null);
        RuleFor(x => x.Steps).NotEmpty().WithMessage("A routing needs at least one step");
        RuleFor(x => x.Steps).Must(RoutingSteps.AreNumberedInOrder)
            .WithMessage("Step numbers must run 1, 2, 3 … without gaps");
        RuleForEach(x => x.Steps).SetValidator(new RoutingStepRequestValidator());
    }
}

public class RoutingStepRequestValidator : AbstractValidator<RoutingStepRequest>
{
    public RoutingStepRequestValidator()
    {
        RuleFor(x => x.OperationTemplateId).NotEmpty();
        RuleFor(x => x.SetupMinutes).GreaterThanOrEqualTo(0);
        RuleFor(x => x.RunMinutesPerUnit).GreaterThanOrEqualTo(0);
        RuleFor(x => x.LagMinutesAfter).GreaterThanOrEqualTo(0);
        RuleFor(x => x.SetupMinutes + x.RunMinutesPerUnit).GreaterThan(0)
            .WithMessage("A step must take time: setup or run time per unit must be positive");
    }
}

public class InstantiateRoutingRequestValidator : AbstractValidator<InstantiateRoutingRequest>
{
    public InstantiateRoutingRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.RequestNameMaxLength);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.LatestEndTs!.Value).GreaterThan(x => x.EarliestStartTs!.Value)
            .WithMessage("LatestEndTs must be after EarliestStartTs")
            .When(x => x.EarliestStartTs.HasValue && x.LatestEndTs.HasValue);
    }
}

internal static class RoutingSteps
{
    /// <summary>Step numbers are 1..n in list order: the order is the routing.</summary>
    public static bool AreNumberedInOrder(IReadOnlyList<RoutingStepRequest> steps)
        => steps.Select((s, i) => s.StepNo == i + 1).All(ok => ok);
}

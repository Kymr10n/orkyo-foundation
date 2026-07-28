using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for resource assignment create/validate. The interval invariant
/// (<c>EndUtc &gt; StartUtc</c>) is the one that matters: a zero-length or inverted window
/// silently matches nothing in the overlap queries rather than failing loudly.
/// </summary>
public class CreateResourceAssignmentRequestValidator : AbstractValidator<CreateResourceAssignmentRequest>
{
    public CreateResourceAssignmentRequestValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc)
            .WithMessage("EndUtc must be after StartUtc");
        RuleFor(x => x.AllocationPercent!.Value).InclusiveBetween(0m, 100m)
            .When(x => x.AllocationPercent.HasValue);
        RuleFor(x => x.AllocationUnits!.Value).GreaterThan(0)
            .When(x => x.AllocationUnits.HasValue);
    }
}

public class ValidateResourceAssignmentRequestValidator : AbstractValidator<ValidateResourceAssignmentRequest>
{
    public ValidateResourceAssignmentRequestValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc)
            .WithMessage("EndUtc must be after StartUtc");
        RuleFor(x => x.AllocationPercent!.Value).InclusiveBetween(0m, 100m)
            .When(x => x.AllocationPercent.HasValue);
        RuleFor(x => x.AllocationUnits!.Value).GreaterThan(0)
            .When(x => x.AllocationUnits.HasValue);
    }
}

public class ValidateResourceAssignmentBatchRequestValidator : AbstractValidator<ValidateResourceAssignmentBatchRequest>
{
    public ValidateResourceAssignmentBatchRequestValidator()
    {
        RuleFor(x => x.Items).NotNull();
        // Each item carries the same invariants; reuse rather than restate them.
        RuleForEach(x => x.Items).SetValidator(new ValidateResourceAssignmentRequestValidator());
    }
}

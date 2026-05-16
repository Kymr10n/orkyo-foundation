using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateResourceGroupRequestValidator : AbstractValidator<CreateResourceGroupRequest>
{
    public CreateResourceGroupRequestValidator()
    {
        RuleFor(x => x.ResourceTypeKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        When(x => !string.IsNullOrWhiteSpace(x.Description), () =>
            RuleFor(x => x.Description!).MaximumLength(1000));
        RuleFor(x => x.DefaultAvailabilityPercent).InclusiveBetween(0, 100);
    }
}

public class UpdateResourceGroupRequestValidator : AbstractValidator<UpdateResourceGroupRequest>
{
    public UpdateResourceGroupRequestValidator()
    {
        When(x => x.Name != null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(255));
        When(x => !string.IsNullOrWhiteSpace(x.Description), () =>
            RuleFor(x => x.Description!).MaximumLength(1000));
        When(x => x.DefaultAvailabilityPercent.HasValue, () =>
            RuleFor(x => x.DefaultAvailabilityPercent!.Value).InclusiveBetween(0, 100));
    }
}

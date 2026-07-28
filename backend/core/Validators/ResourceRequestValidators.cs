using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for the resource surface. Allocation mode is checked against
/// <see cref="AllocationModes"/> rather than a literal set so the vocabulary has one owner.
/// </summary>
public class CreateResourceRequestValidator : AbstractValidator<CreateResourceRequest>
{
    public static readonly string[] KnownAllocationModes =
        [AllocationModes.Exclusive, AllocationModes.Fractional, AllocationModes.ConcurrentCapacity];

    public CreateResourceRequestValidator()
    {
        RuleFor(x => x.ResourceTypeKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.SiteNameMaxLength);
        RuleFor(x => x.Description!).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.ExternalReference!).MaximumLength(200).When(x => x.ExternalReference is not null);
        RuleFor(x => x.AllocationMode).NotEmpty()
            .Must(m => KnownAllocationModes.Contains(m))
            .WithMessage($"AllocationMode must be one of: {string.Join(", ", KnownAllocationModes)}");
        RuleFor(x => x.BaseAvailabilityPercent).InclusiveBetween(0, 100);
    }
}

public class UpdateResourceRequestValidator : AbstractValidator<UpdateResourceRequest>
{
    public UpdateResourceRequestValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(DomainLimits.SiteNameMaxLength)
            .When(x => x.Name is not null);
        RuleFor(x => x.Description!).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.ExternalReference!).MaximumLength(200).When(x => x.ExternalReference is not null);
        RuleFor(x => x.AllocationMode!)
            .Must(m => CreateResourceRequestValidator.KnownAllocationModes.Contains(m))
            .WithMessage($"AllocationMode must be one of: {string.Join(", ", CreateResourceRequestValidator.KnownAllocationModes)}")
            .When(x => x.AllocationMode is not null);
        RuleFor(x => x.BaseAvailabilityPercent!.Value).InclusiveBetween(0, 100)
            .When(x => x.BaseAvailabilityPercent.HasValue);
    }
}

public class UpsertResourceCapabilityRequestValidator : AbstractValidator<UpsertResourceCapabilityRequest>
{
    public UpsertResourceCapabilityRequestValidator() =>
        RuleFor(x => x.CriterionId).NotEmpty();
}

public class UpdateCriterionApplicabilityRequestValidator : AbstractValidator<UpdateCriterionApplicabilityRequest>
{
    public UpdateCriterionApplicabilityRequestValidator()
    {
        // Null means "leave unchanged"; an empty-string key inside the list is always a client bug.
        RuleForEach(x => x.ResourceTypeKeys!).NotEmpty().MaximumLength(100)
            .When(x => x.ResourceTypeKeys is not null);
    }
}

public class SetResourceGroupMembersRequestValidator : AbstractValidator<SetResourceGroupMembersRequest>
{
    public SetResourceGroupMembersRequestValidator()
    {
        RuleFor(x => x.ResourceIds).NotNull();
        RuleForEach(x => x.ResourceIds).NotEmpty()
            .WithMessage("ResourceIds must not contain empty GUIDs");
    }
}

public class LinkUserToPersonProfileRequestValidator : AbstractValidator<LinkUserToPersonProfileRequest>
{
    public LinkUserToPersonProfileRequestValidator() =>
        RuleFor(x => x.UserId).NotEmpty();
}

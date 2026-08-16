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

        // Placement shape. These are safe to apply unconditionally even though only placeable
        // types may carry placement: a non-placeable request that sends any of it is rejected by
        // ResourceService.ValidatePlacement, which is where the type is known. Here we only say
        // what a well-formed shape looks like — the validator never sees the resource type.
        RuleFor(x => x.Code!).MaximumLength(DomainLimits.ResourceCodeMaxLength)
            .When(x => x.Code is not null);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(1);
        RuleFor(x => x.Geometry!)
            .Must(g => g.IsValid())
            .WithMessage(x => $"Invalid geometry: {x.Geometry!.Type} type requires correct number of coordinates")
            .When(x => x.Geometry is not null);
        RuleFor(x => x.Geometry)
            .NotNull().WithMessage("Physical resources must have geometry")
            .When(x => x.IsPhysical);
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

        // Placement shape — see the note on the create validator. There is no physical-implies-
        // geometry rule here because IsPhysical is deliberately absent from the update request:
        // a resource cannot stop being physical, so geometry can never be orphaned by an update.
        RuleFor(x => x.Code!).MaximumLength(DomainLimits.ResourceCodeMaxLength)
            .When(x => x.Code is not null);
        RuleFor(x => x.Capacity!.Value).GreaterThanOrEqualTo(1)
            .When(x => x.Capacity.HasValue);
        RuleFor(x => x.Geometry!)
            .Must(g => g.IsValid())
            .WithMessage(x => $"Invalid geometry: {x.Geometry!.Type} type requires correct number of coordinates")
            .When(x => x.Geometry is not null);
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

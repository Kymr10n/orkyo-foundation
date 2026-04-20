using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpdateSpaceGroupRequestValidator : SpaceGroupRequestValidator<UpdateSpaceGroupRequest>
{
    public UpdateSpaceGroupRequestValidator()
    {
        When(x => x.Name != null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(DomainLimits.SpaceGroupNameMaxLength));
    }
}

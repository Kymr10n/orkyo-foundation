using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateSpaceGroupRequestValidator : SpaceGroupRequestValidator<CreateSpaceGroupRequest>
{
    public CreateSpaceGroupRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.SpaceGroupNameMaxLength);
    }
}

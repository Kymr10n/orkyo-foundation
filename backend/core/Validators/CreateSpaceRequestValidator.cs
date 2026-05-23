using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateSpaceRequestValidator : SpaceGeometryValidator<CreateSpaceRequest>
{
    public CreateSpaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Geometry)
            .NotNull().WithMessage("Physical spaces must have geometry")
            .When(x => x.IsPhysical);
    }
}

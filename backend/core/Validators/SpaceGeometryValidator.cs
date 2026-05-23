using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Base validator for space create/update requests — shared geometry validation rule.
/// </summary>
public abstract class SpaceGeometryValidator<T> : AbstractValidator<T> where T : ISpaceGeometryRequest
{
    protected SpaceGeometryValidator()
    {
        When(x => x.Geometry != null, () =>
            RuleFor(x => x.Geometry!)
                .Must(g => g.IsValid())
                .WithMessage(x => $"Invalid geometry: {x.Geometry!.Type} type requires correct number of coordinates"));
    }
}

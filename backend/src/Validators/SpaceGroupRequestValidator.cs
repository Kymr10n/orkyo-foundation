using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Base validator for space group create/update requests — shared Color and Description rules.
/// </summary>
public abstract class SpaceGroupRequestValidator<T> : AbstractValidator<T> where T : ISpaceGroupRequest
{
    protected SpaceGroupRequestValidator()
    {
        When(x => !string.IsNullOrWhiteSpace(x.Color), () =>
            RuleFor(x => x.Color!)
                .Matches(@"^#[0-9A-Fa-f]{6}$")
                .WithMessage("Color must be a valid hex color (#RRGGBB)"));

        When(x => !string.IsNullOrWhiteSpace(x.Description), () =>
            RuleFor(x => x.Description!).MaximumLength(DomainLimits.SpaceGroupDescriptionMaxLength));
    }
}

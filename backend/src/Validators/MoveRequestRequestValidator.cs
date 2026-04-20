using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class MoveRequestRequestValidator : AbstractValidator<MoveRequestRequest>
{
    public MoveRequestRequestValidator()
    {
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("Sort order must be non-negative");
    }
}

using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class RemoveMfaRequestValidator : AbstractValidator<RemoveMfaRequest>
{
    public RemoveMfaRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required");
    }
}

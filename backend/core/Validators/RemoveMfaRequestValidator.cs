using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class RemoveMfaRequestValidator : AbstractValidator<RemoveMfaRequest>
{
    public RemoveMfaRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required");
        RuleFor(x => x.CurrentCode).NotEmpty().WithMessage("Current authenticator code is required")
            .Matches(@"\A[0-9]{6}\z").WithMessage("Authenticator code must be 6 digits");
    }
}

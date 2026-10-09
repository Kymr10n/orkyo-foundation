using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class RemovePasskeyRequestValidator : AbstractValidator<RemovePasskeyRequest>
{
    public RemovePasskeyRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required");
        // Optional: only a TOTP user needs it, and Keycloak answers 400 if it is missing for one.
        RuleFor(x => x.CurrentCode).Matches(@"\A[0-9]{6}\z").WithMessage("Authenticator code must be 6 digits")
            .When(x => !string.IsNullOrEmpty(x.CurrentCode));
    }
}

public class RenamePasskeyRequestValidator : AbstractValidator<RenamePasskeyRequest>
{
    public RenamePasskeyRequestValidator()
    {
        RuleFor(x => x.Label).NotEmpty().WithMessage("Label is required")
            .MaximumLength(DomainLimits.PasskeyLabelMaxLength)
            .Matches(@"\A[^\p{C}]+\z").WithMessage("Label must not contain control characters");
    }
}

using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public class InviteUserRequestValidator : AbstractValidator<InviteUserRequest>
{
    public InviteUserRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Email must be a valid email address");
        RuleFor(x => x.Role).IsInEnum().WithMessage("Role is not valid");
    }
}

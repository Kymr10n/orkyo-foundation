using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().WithMessage("Token is required");
        RuleFor(x => x.DisplayName).NotEmpty().WithMessage("Display name is required");
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");
    }
}

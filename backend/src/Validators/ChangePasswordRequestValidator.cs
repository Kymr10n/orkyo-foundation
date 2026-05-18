using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required");
        RuleFor(x => x.NewPassword).NotEmpty().WithMessage("New password is required");
        RuleFor(x => x).Must(x => x.NewPassword == x.ConfirmPassword)
            .WithMessage("Passwords do not match")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
    }
}

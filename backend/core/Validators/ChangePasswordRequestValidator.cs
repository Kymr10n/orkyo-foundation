using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().WithMessage("Current password is required");
        RuleFor(x => x.CurrentCode).Matches(@"\A[0-9]{6}\z").WithMessage("Authenticator code must be 6 digits")
            .When(x => !string.IsNullOrEmpty(x.CurrentCode));
        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(TenantSettings.DefaultPasswordMinLength)
            .WithMessage($"New password must be at least {TenantSettings.DefaultPasswordMinLength} characters");
        RuleFor(x => x).Must(x => x.NewPassword == x.ConfirmPassword)
            .WithMessage("Passwords do not match")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
    }
}

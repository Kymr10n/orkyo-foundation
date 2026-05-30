using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("Invalid email format");
        RuleFor(x => x.Password).NotEmpty()
            .MinimumLength(TenantSettings.DefaultPasswordMinLength)
            .WithMessage($"Password must be at least {TenantSettings.DefaultPasswordMinLength} characters");
    }
}

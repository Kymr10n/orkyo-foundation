using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpsertPersonProfileRequestValidator : AbstractValidator<UpsertPersonProfileRequest>
{
    public UpsertPersonProfileRequestValidator()
    {
        // Email column is CITEXT (no width); enforce a sensible upper bound and basic shape.
        RuleFor(x => x.Email)
            .MaximumLength(254) // RFC 5321
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}

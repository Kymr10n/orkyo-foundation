using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpsertPersonProfileRequestValidator : AbstractValidator<UpsertPersonProfileRequest>
{
    public UpsertPersonProfileRequestValidator()
    {
        // Column widths mirror migration 1400.foundation.people.sql.
        // Email is CITEXT (no width); enforce a sensible upper bound and basic shape.
        RuleFor(x => x.Email)
            .MaximumLength(254) // RFC 5321
            .EmailAddress().WithMessage("Email must be a valid email address.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.JobTitle)
            .MaximumLength(200);

        RuleFor(x => x.Department)
            .MaximumLength(200);
    }
}

using Api.Constants;
using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.FirstName) || !string.IsNullOrWhiteSpace(x.LastName))
            .WithMessage("At least one name field is required");
        // Keycloak is updated before the display name is written; a name too long for
        // users.display_name would change the one and fail the other.
        RuleFor(x => x.FirstName).MaximumLength(DomainLimits.PersonNamePartMaxLength);
        RuleFor(x => x.LastName).MaximumLength(DomainLimits.PersonNamePartMaxLength);
    }
}

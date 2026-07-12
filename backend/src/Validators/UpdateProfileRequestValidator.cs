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
    }
}

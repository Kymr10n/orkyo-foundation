using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public class UpdateUserRoleRequestValidator : AbstractValidator<UpdateUserRoleRequest>
{
    public UpdateUserRoleRequestValidator()
    {
        RuleFor(x => x.Role).IsInEnum().WithMessage("Role is not valid");
    }
}

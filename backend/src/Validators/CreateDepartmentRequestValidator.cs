using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateDepartmentRequestValidator : AbstractValidator<CreateDepartmentRequest>
{
    public CreateDepartmentRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code!).MaximumLength(50)
            .When(x => x.Code is not null);
        RuleFor(x => x.Description!).MaximumLength(2000)
            .When(x => x.Description is not null);
    }
}

public class UpdateDepartmentRequestValidator : AbstractValidator<UpdateDepartmentRequest>
{
    public UpdateDepartmentRequestValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200)
            .When(x => x.Name is not null);
        RuleFor(x => x.Code!).MaximumLength(50)
            .When(x => x.Code is not null);
        RuleFor(x => x.Description!).MaximumLength(2000)
            .When(x => x.Description is not null);
    }
}

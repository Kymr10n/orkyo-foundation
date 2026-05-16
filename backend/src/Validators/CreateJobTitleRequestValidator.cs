using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateJobTitleRequestValidator : AbstractValidator<CreateJobTitleRequest>
{
    public CreateJobTitleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000)
            .When(x => x.Description is not null);
    }
}

public class UpdateJobTitleRequestValidator : AbstractValidator<UpdateJobTitleRequest>
{
    public UpdateJobTitleRequestValidator()
    {
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200)
            .When(x => x.Name is not null);
        RuleFor(x => x.Description!).MaximumLength(2000)
            .When(x => x.Description is not null);
    }
}

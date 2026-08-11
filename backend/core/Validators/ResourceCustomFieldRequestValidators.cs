using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateResourceCustomFieldRequestValidator : AbstractValidator<CreateResourceCustomFieldRequest>
{
    public CreateResourceCustomFieldRequestValidator()
    {
        // Same shape as a resource type key, and for the same reason: it is a stable identifier
        // inside a stored document, not a label.
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(ResourceTypeKeyRules.Pattern).WithMessage(ResourceTypeKeyRules.Message);

        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);

        RuleFor(x => x.DataType)
            .Must(CustomFieldDataTypes.All.Contains)
            .WithMessage($"DataType must be one of: {string.Join(", ", CustomFieldDataTypes.All)}");

        // 2000 bounds this definition's own description text. It is not the cap on the values
        // resources store for the field — that lives in ResourceCustomFieldService and answers
        // a different question (jsonb payload size).
        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

public class UpdateResourceCustomFieldRequestValidator : AbstractValidator<UpdateResourceCustomFieldRequest>
{
    public UpdateResourceCustomFieldRequestValidator()
    {
        When(x => x.Label is not null, () =>
            RuleFor(x => x.Label!).NotEmpty().MaximumLength(100));

        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

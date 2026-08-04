using System.Text.Json;
using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Keys are used as stable identifiers in API filters and metadata documents, so they are
/// restricted to the same lower-snake shape the database CHECK constraint enforces for fields.
/// </summary>
internal static class ResourceTypeKeyRules
{
    public const string Pattern = "^[a-z][a-z0-9_]{0,49}$";
    public const string Message =
        "Key must start with a lowercase letter and contain only lowercase letters, numbers, and underscores";
}

public class CreateResourceTypeRequestValidator : AbstractValidator<CreateResourceTypeRequest>
{
    public CreateResourceTypeRequestValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(ResourceTypeKeyRules.Pattern).WithMessage(ResourceTypeKeyRules.Message);

        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayNamePlural).NotEmpty().MaximumLength(100);

        When(x => x.Icon is not null, () =>
            RuleFor(x => x.Icon!).MaximumLength(50));
    }
}

public class UpdateResourceTypeRequestValidator : AbstractValidator<UpdateResourceTypeRequest>
{
    public UpdateResourceTypeRequestValidator()
    {
        When(x => x.DisplayName is not null, () =>
            RuleFor(x => x.DisplayName!).NotEmpty().MaximumLength(100));

        When(x => x.DisplayNamePlural is not null, () =>
            RuleFor(x => x.DisplayNamePlural!).NotEmpty().MaximumLength(100));

        When(x => x.Icon is not null, () =>
            RuleFor(x => x.Icon!).MaximumLength(50));
    }
}

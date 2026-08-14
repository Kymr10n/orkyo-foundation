using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateListDefinitionRequestValidator : AbstractValidator<CreateListDefinitionRequest>
{
    public CreateListDefinitionRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);

        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

public class UpdateListDefinitionRequestValidator : AbstractValidator<UpdateListDefinitionRequest>
{
    public UpdateListDefinitionRequestValidator()
    {
        When(x => x.Name is not null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(100));

        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

public class CreateListColumnRequestValidator : AbstractValidator<CreateListColumnRequest>
{
    /// <summary>
    /// Option text is bounded because options ride inside the column's jsonb document and are
    /// rendered as menu entries. The count is not: criteria enum values are uncapped, and a
    /// column with many options is a tenant's business.
    /// </summary>
    public const int MaxOptionLength = 100;

    public CreateListColumnRequestValidator()
    {
        // Same shape as a custom field key, and for the same reason: it is a stable identifier
        // inside a stored row, not a label.
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(ResourceTypeKeyRules.Pattern).WithMessage(ResourceTypeKeyRules.Message);

        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);

        RuleFor(x => x.DataType)
            .Must(ListColumnDataTypes.All.Contains)
            .WithMessage($"DataType must be one of: {string.Join(", ", ListColumnDataTypes.All)}");

        // Options belong to select and nowhere else — the same rule the CHECK constraint holds,
        // stated here so the caller gets a 400 naming the field rather than a constraint violation.
        When(x => x.DataType != ListColumnDataTypes.Select, () =>
            RuleFor(x => x.Options)
                .Null().WithMessage("Options apply to select columns only"));

        When(x => x.Options is not null, () =>
            RuleForEach(x => x.Options!).NotEmpty().MaximumLength(MaxOptionLength));

        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

public class UpdateListColumnRequestValidator : AbstractValidator<UpdateListColumnRequest>
{
    public UpdateListColumnRequestValidator()
    {
        When(x => x.Label is not null, () =>
            RuleFor(x => x.Label!).NotEmpty().MaximumLength(100));

        // Whether options are allowed at all depends on the column's data type, which this request
        // cannot see — the service checks that against the stored column. Shape only here.
        When(x => x.Options is not null, () =>
            RuleForEach(x => x.Options!).NotEmpty()
                .MaximumLength(CreateListColumnRequestValidator.MaxOptionLength));

        When(x => x.Description is not null, () =>
            RuleFor(x => x.Description!).MaximumLength(2000));
    }
}

public class CreateListInstanceRequestValidator : AbstractValidator<CreateListInstanceRequest>
{
    public CreateListInstanceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class UpdateListInstanceRequestValidator : AbstractValidator<UpdateListInstanceRequest>
{
    public UpdateListInstanceRequestValidator()
    {
        When(x => x.Name is not null, () =>
            RuleFor(x => x.Name!).NotEmpty().MaximumLength(100));
    }
}

public class ListRowRequestValidator : AbstractValidator<ListRowRequest>
{
    public ListRowRequestValidator()
    {
        // Cell values are validated against the definition's columns in ListRowService, which is
        // the only place that knows the shape. All this can say is that a row carries a document.
        RuleFor(x => x.Values).NotNull();
    }
}

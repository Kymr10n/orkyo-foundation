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

        When(x => x.Icon is not null, () =>
            RuleFor(x => x.Icon!).MaximumLength(50));
    }
}

public class CreateResourceTypeFieldRequestValidator : AbstractValidator<CreateResourceTypeFieldRequest>
{
    public CreateResourceTypeFieldRequestValidator()
    {
        RuleFor(x => x.Key)
            .NotEmpty()
            .MaximumLength(50)
            .Matches(ResourceTypeKeyRules.Pattern).WithMessage(ResourceTypeKeyRules.Message);

        RuleFor(x => x.Label).NotEmpty().MaximumLength(100);

        RuleFor(x => x.DataType)
            .Must(ResourceFieldDataTypes.IsKnown)
            .WithMessage($"Data type must be one of: {string.Join(", ", ResourceFieldDataTypes.All)}");

        When(x => x.DataType == ResourceFieldDataTypes.Select, () =>
            RuleFor(x => x.Options)
                .Must(ResourceFieldRules.HasSelectOptions)
                .WithMessage("A select field requires an options object of the form {\"values\":[\"…\"]}"));

        RuleFor(x => x.Validation)
            .Must(ResourceFieldRules.IsValidationShape)
            .WithMessage("Validation must be an object with optional min, max, regex, and maxLength members");
    }
}

public class UpdateResourceTypeFieldRequestValidator : AbstractValidator<UpdateResourceTypeFieldRequest>
{
    public UpdateResourceTypeFieldRequestValidator()
    {
        When(x => x.Label is not null, () =>
            RuleFor(x => x.Label!).NotEmpty().MaximumLength(100));

        RuleFor(x => x.Validation)
            .Must(ResourceFieldRules.IsValidationShape)
            .WithMessage("Validation must be an object with optional min, max, regex, and maxLength members");
    }
}

internal static class ResourceFieldRules
{
    public static bool HasSelectOptions(JsonElement? options)
    {
        if (options is not { } o
            || o.ValueKind != JsonValueKind.Object
            || !o.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
            return false;

        return values.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String);
    }

    /// <summary>
    /// Shape-only check: members must have the right JSON kind. Whether the constraints make
    /// sense for the field's data type is enforced when values are validated.
    /// </summary>
    public static bool IsValidationShape(JsonElement? validation)
    {
        if (validation is not { } v) return true;
        if (v.ValueKind == JsonValueKind.Null) return true;
        if (v.ValueKind != JsonValueKind.Object) return false;

        foreach (var member in v.EnumerateObject())
        {
            var ok = member.Name switch
            {
                "min" or "max" => member.Value.ValueKind == JsonValueKind.Number,
                "maxLength" => member.Value.ValueKind == JsonValueKind.Number
                               && member.Value.TryGetInt32(out var len) && len > 0,
                "regex" => member.Value.ValueKind == JsonValueKind.String,
                _ => false,
            };
            if (!ok) return false;
        }

        return true;
    }
}

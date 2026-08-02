using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Models;

namespace Api.Services;

/// <summary>
/// Checks a value against its criterion's data type and optional constraints.
///
/// Capability values used to be written as raw JSONB with no check at all beyond applicability:
/// a Number criterion would happily store the string "banana", and the mismatch only surfaced
/// later as a silent non-match in the solver. This closes that hole and, in doing so, gives
/// criteria the value-validation the retired resource_type_fields system had — the reason the
/// two systems could be collapsed into one.
///
/// Cross-entity and stateful (the criterion comes from the database), so it is a service
/// validator rather than a FluentValidation rule set — see docs/validation.md.
/// </summary>
public interface ICriterionValueValidator
{
    /// <summary>
    /// Returns null when the value is acceptable, otherwise a human-readable reason naming
    /// the criterion — the message reaches the user, who thinks in criterion names, not ids.
    /// </summary>
    string? Validate(CriterionInfo criterion, JsonElement value);
}

public class CriterionValueValidator : ICriterionValueValidator
{
    // Guards against catastrophic backtracking in tenant-authored patterns.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public string? Validate(CriterionInfo criterion, JsonElement value)
    {
        // An explicit null clears the value. Required-ness is a per-resource-type concern
        // (criterion_resource_types.is_required), enforced where the whole resource is saved,
        // not here where a single value is checked.
        if (value.ValueKind == JsonValueKind.Null) return null;

        return criterion.DataType switch
        {
            CriterionDataType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? null
                : Mismatch(criterion, "true or false"),
            CriterionDataType.Number => value.ValueKind == JsonValueKind.Number
                ? ValidateNumber(criterion, value.GetDouble())
                : Mismatch(criterion, "a number"),
            CriterionDataType.String => value.ValueKind == JsonValueKind.String
                ? ValidateText(criterion, value.GetString()!)
                : Mismatch(criterion, "a text value"),
            CriterionDataType.Date => value.ValueKind == JsonValueKind.String
                ? ValidateDate(criterion, value.GetString()!)
                : Mismatch(criterion, "a date"),
            CriterionDataType.Enum => value.ValueKind == JsonValueKind.String
                ? ValidateEnum(criterion, value.GetString()!)
                : Mismatch(criterion, "one of the allowed values"),
            _ => null,
        };
    }

    private static string? ValidateNumber(CriterionInfo criterion, double number)
    {
        if (TryGetConstraint(criterion, "min", out var min)
            && min.ValueKind == JsonValueKind.Number && number < min.GetDouble())
            return Issue(criterion, $"must be at least {min.GetDouble()}");

        if (TryGetConstraint(criterion, "max", out var max)
            && max.ValueKind == JsonValueKind.Number && number > max.GetDouble())
            return Issue(criterion, $"must be at most {max.GetDouble()}");

        return null;
    }

    private static string? ValidateText(CriterionInfo criterion, string text)
    {
        if (TryGetConstraint(criterion, "maxLength", out var maxLength)
            && maxLength.ValueKind == JsonValueKind.Number
            && maxLength.TryGetInt32(out var limit)
            && text.Length > limit)
            return Issue(criterion, $"must be at most {limit} characters");

        if (TryGetConstraint(criterion, "regex", out var regex) && regex.ValueKind == JsonValueKind.String)
        {
            try
            {
                if (!Regex.IsMatch(text, regex.GetString()!, RegexOptions.None, RegexTimeout))
                    return Issue(criterion, "does not match the required format");
            }
            catch (RegexMatchTimeoutException)
            {
                // Report rather than throw: a tenant-authored pattern must not 500 the request.
                return Issue(criterion, "could not be validated: the pattern took too long to evaluate");
            }
            catch (ArgumentException)
            {
                return Issue(criterion, "could not be validated: the pattern is not a valid regular expression");
            }
        }

        return null;
    }

    /// <summary>yyyy-MM-dd only — the same wire format the field system used, so values transfer.</summary>
    private static string? ValidateDate(CriterionInfo criterion, string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? null
            : Mismatch(criterion, "a date in yyyy-MM-dd format");

    private static string? ValidateEnum(CriterionInfo criterion, string text)
    {
        // No declared values = nothing to check against, rather than "reject everything".
        var allowed = criterion.EnumValues;
        if (allowed is null || allowed.Count == 0) return null;

        return allowed.Contains(text, StringComparer.Ordinal)
            ? null
            : Issue(criterion, $"must be one of: {string.Join(", ", allowed)}");
    }

    private static bool TryGetConstraint(CriterionInfo criterion, string name, out JsonElement value)
    {
        value = default;
        return criterion.Validation is { ValueKind: JsonValueKind.Object } v
            && v.TryGetProperty(name, out value);
    }

    private static string Mismatch(CriterionInfo criterion, string expected) =>
        $"'{criterion.Name}' expects {expected}";

    private static string Issue(CriterionInfo criterion, string problem) =>
        $"'{criterion.Name}' {problem}";
}

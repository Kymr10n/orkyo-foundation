using System.Globalization;
using System.Text.Json;
using Api.Models;

namespace Api.Services;

/// <summary>
/// What a typed value is allowed to be, for the two places that store one: a custom field on a
/// resource, and a cell in a list row. Both write into a jsonb document, both are validated
/// against a tenant-defined data type, and the rules are identical — so they live here once
/// rather than being re-derived per caller and drifting.
/// </summary>
/// <remarks>
/// Callers pass the <c>subject</c> that names the thing being validated ("Custom field 'Serial'",
/// "Column 'Mileage'"); it is prefixed onto every message, so each surface reads in its own terms
/// while the rules stay shared.
/// </remarks>
public static class CustomFieldValueRules
{
    // Values are stored whole in a jsonb document and shipped back with every read, so they are
    // bounded here — nothing else bounds them. Generous enough for a note, small enough that a
    // row stays a row.
    public const int MaxTextLength = 2000;
    public const int MaxUrlLength = 2048;

    /// <summary>Null, and the empty string a cleared text input sends, both mean "no value".</summary>
    public static bool IsEmpty(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || (value.ValueKind is JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <summary>
    /// Throws <see cref="ArgumentException"/> when <paramref name="value"/> does not match
    /// <paramref name="dataType"/>. Callers skip empty values first — whether a value may be
    /// absent is a required question, not a type question.
    /// </summary>
    /// <param name="options">
    /// Declared options for <c>select</c>, ignored otherwise. No declared options means nothing to
    /// check against rather than "reject everything", matching
    /// <see cref="CriterionValueValidator"/>'s enum semantics.
    /// </param>
    public static void Validate(
        string subject, string dataType, JsonElement value, IReadOnlyList<string>? options = null)
    {
        switch (dataType)
        {
            case CustomFieldDataTypes.Text:
                Expect(subject, value, JsonValueKind.String, "text");
                if (value.GetString()!.Length > MaxTextLength)
                    throw Mismatch(subject, $"at most {MaxTextLength} characters");
                break;

            case CustomFieldDataTypes.Number:
                if (value.ValueKind is not JsonValueKind.Number)
                    throw Mismatch(subject, "a number");
                // A JSON number can carry an exponent Postgres numeric cannot hold, in either
                // direction — 1e1000000 overflows a double, 1e-1000000 quietly becomes 0.0 while
                // the raw token still reaches jsonb. Either way it is a 22003 nobody maps, which
                // surfaces as a 500. TryGetDecimal accepts the range jsonb actually stores.
                if (!value.TryGetDecimal(out _))
                    throw Mismatch(subject, "a number within the usual range");
                break;

            case CustomFieldDataTypes.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw Mismatch(subject, "true or false");
                break;

            case CustomFieldDataTypes.Date:
                Expect(subject, value, JsonValueKind.String, "a date");
                if (!DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out _))
                    throw Mismatch(subject, "a date in yyyy-MM-dd format");
                break;

            case CustomFieldDataTypes.Url:
                Expect(subject, value, JsonValueKind.String, "a URL");
                if (value.GetString()!.Length > MaxUrlLength)
                    throw Mismatch(subject, $"at most {MaxUrlLength} characters");
                // Absolute and http(s) only: the value is rendered as a link, and a relative or
                // javascript: URL there is a navigation the tenant did not intend.
                if (!Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw Mismatch(subject, "an absolute http(s) URL");
                break;

            case ListColumnDataTypes.Select:
                Expect(subject, value, JsonValueKind.String, "one of the declared options");
                if (options is null || options.Count == 0) break;
                if (!options.Contains(value.GetString()!, StringComparer.Ordinal))
                    throw Mismatch(subject, $"one of: {string.Join(", ", options)}");
                break;

            case ListColumnDataTypes.RowRef:
                // Shape only. That the id names a row of this same instance, is not the row
                // itself, and closes no cycle needs the other rows, so it stays in the service.
                Expect(subject, value, JsonValueKind.String, "a row of this list");
                if (!Guid.TryParse(value.GetString(), out _))
                    throw Mismatch(subject, "a row of this list");
                break;

            default:
                // Unreachable while the CHECK constraints and the create validators agree on the set.
                throw new InvalidOperationException($"Unknown data type '{dataType}'");
        }
    }

    private static void Expect(string subject, JsonElement value, JsonValueKind kind, string expected)
    {
        if (value.ValueKind != kind) throw Mismatch(subject, expected);
    }

    private static ArgumentException Mismatch(string subject, string expected) =>
        new($"{subject} expects {expected}");
}

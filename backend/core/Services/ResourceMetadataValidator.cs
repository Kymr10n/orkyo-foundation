using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Constants;
using Api.Models;
using Api.Repositories;

namespace Api.Services;

/// <summary>
/// Validates a resource's custom field values against its type's field definitions.
/// Cross-entity and stateful (definitions live in the database), so this is a service
/// validator rather than a FluentValidation rule set — see docs/validation.md.
/// </summary>
public interface IResourceMetadataValidator
{
    /// <summary>
    /// Validates <paramref name="metadata"/> against the active field definitions of
    /// <paramref name="resourceTypeId"/>. A null document is treated as empty, which still
    /// fails when the type has required fields and <paramref name="requireComplete"/> is set.
    /// </summary>
    /// <param name="requireComplete">
    /// True on create and on a full metadata replacement: required fields must be present.
    /// </param>
    Task<MetadataValidationResult> ValidateAsync(
        Guid resourceTypeId,
        IReadOnlyDictionary<string, JsonElement>? metadata,
        bool requireComplete = true,
        CancellationToken ct = default);
}

public class ResourceMetadataValidator(IResourceTypeFieldRepository fieldRepository)
    : IResourceMetadataValidator
{
    // Guards against catastrophic backtracking in tenant-authored patterns.
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public async Task<MetadataValidationResult> ValidateAsync(
        Guid resourceTypeId,
        IReadOnlyDictionary<string, JsonElement>? metadata,
        bool requireComplete = true,
        CancellationToken ct = default)
    {
        var blockers = new List<MetadataValidationIssue>();
        var warnings = new List<MetadataValidationIssue>();

        var definitions = await fieldRepository.GetByTypeAsync(resourceTypeId, includeInactive: true, ct);
        var byKey = definitions.ToDictionary(f => f.Key, StringComparer.Ordinal);

        foreach (var (key, value) in metadata ?? new Dictionary<string, JsonElement>())
        {
            if (!byKey.TryGetValue(key, out var field))
            {
                blockers.Add(new MetadataValidationIssue
                {
                    FieldKey = key,
                    Message = $"'{key}' is not a field of this resource type",
                });
                continue;
            }

            if (!field.IsActive)
            {
                warnings.Add(new MetadataValidationIssue
                {
                    FieldKey = key,
                    Message = $"Field '{key}' is no longer active; the value is stored but unused",
                });
                continue;
            }

            // An explicit null clears the value — only a problem when the field is required.
            if (value.ValueKind == JsonValueKind.Null)
            {
                if (field.IsRequired)
                    blockers.Add(Required(field));
                continue;
            }

            ValidateValue(field, value, blockers);
        }

        if (requireComplete)
        {
            foreach (var field in definitions.Where(f => f.IsActive && f.IsRequired))
            {
                var present = metadata is not null
                    && metadata.TryGetValue(field.Key, out var v)
                    && v.ValueKind != JsonValueKind.Null;
                if (!present && blockers.All(b => b.FieldKey != field.Key))
                    blockers.Add(Required(field));
            }
        }

        return new MetadataValidationResult { Blockers = blockers, Warnings = warnings };
    }

    private static MetadataValidationIssue Required(ResourceTypeFieldInfo field) => new()
    {
        FieldKey = field.Key,
        Message = $"'{field.Label}' is required",
    };

    private static void ValidateValue(
        ResourceTypeFieldInfo field, JsonElement value, List<MetadataValidationIssue> blockers)
    {
        switch (field.DataType)
        {
            case ResourceFieldDataTypes.Text:
                if (!Expect(field, value, JsonValueKind.String, "a text value", blockers)) return;
                ValidateText(field, value.GetString()!, blockers);
                return;

            case ResourceFieldDataTypes.Number:
                if (value.ValueKind != JsonValueKind.Number)
                {
                    blockers.Add(Mismatch(field, "a number"));
                    return;
                }
                ValidateNumber(field, value.GetDouble(), blockers);
                return;

            case ResourceFieldDataTypes.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    blockers.Add(Mismatch(field, "true or false"));
                return;

            case ResourceFieldDataTypes.Date:
                if (!Expect(field, value, JsonValueKind.String, "a date", blockers)) return;
                if (!DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    blockers.Add(Mismatch(field, "a date in yyyy-MM-dd format"));
                return;

            case ResourceFieldDataTypes.Select:
                if (!Expect(field, value, JsonValueKind.String, "one of the allowed options", blockers)) return;
                ValidateSelect(field, value.GetString()!, blockers);
                return;
        }
    }

    private static void ValidateText(
        ResourceTypeFieldInfo field, string text, List<MetadataValidationIssue> blockers)
    {
        if (field.IsRequired && string.IsNullOrWhiteSpace(text))
        {
            blockers.Add(Required(field));
            return;
        }

        if (TryGetValidation(field, "maxLength", out var maxLength)
            && maxLength.ValueKind == JsonValueKind.Number
            && maxLength.TryGetInt32(out var limit)
            && text.Length > limit)
        {
            blockers.Add(Issue(field, $"must be at most {limit} characters"));
        }

        if (TryGetValidation(field, "regex", out var regex)
            && regex.ValueKind == JsonValueKind.String)
        {
            var pattern = regex.GetString()!;
            try
            {
                if (!Regex.IsMatch(text, pattern, RegexOptions.None, RegexTimeout))
                    blockers.Add(Issue(field, "does not match the required format"));
            }
            catch (RegexMatchTimeoutException)
            {
                blockers.Add(Issue(field, "could not be validated: the field's pattern took too long to evaluate"));
            }
            catch (ArgumentException)
            {
                blockers.Add(Issue(field, "could not be validated: the field's pattern is not a valid regular expression"));
            }
        }
    }

    private static void ValidateNumber(
        ResourceTypeFieldInfo field, double number, List<MetadataValidationIssue> blockers)
    {
        if (TryGetValidation(field, "min", out var min)
            && min.ValueKind == JsonValueKind.Number && number < min.GetDouble())
            blockers.Add(Issue(field, $"must be at least {min.GetDouble()}"));

        if (TryGetValidation(field, "max", out var max)
            && max.ValueKind == JsonValueKind.Number && number > max.GetDouble())
            blockers.Add(Issue(field, $"must be at most {max.GetDouble()}"));
    }

    private static void ValidateSelect(
        ResourceTypeFieldInfo field, string selected, List<MetadataValidationIssue> blockers)
    {
        var allowed = GetSelectOptions(field);
        if (allowed.Count > 0 && !allowed.Contains(selected))
            blockers.Add(Issue(field, $"must be one of: {string.Join(", ", allowed)}"));
    }

    private static List<string> GetSelectOptions(ResourceTypeFieldInfo field)
    {
        if (field.Options is not { } options
            || options.ValueKind != JsonValueKind.Object
            || !options.TryGetProperty("values", out var values)
            || values.ValueKind != JsonValueKind.Array)
            return [];

        return values.EnumerateArray()
            .Where(v => v.ValueKind == JsonValueKind.String)
            .Select(v => v.GetString()!)
            .ToList();
    }

    private static bool TryGetValidation(ResourceTypeFieldInfo field, string name, out JsonElement value)
    {
        value = default;
        return field.Validation is { } validation
            && validation.ValueKind == JsonValueKind.Object
            && validation.TryGetProperty(name, out value);
    }

    private static bool Expect(
        ResourceTypeFieldInfo field, JsonElement value, JsonValueKind expected,
        string description, List<MetadataValidationIssue> blockers)
    {
        if (value.ValueKind == expected) return true;
        blockers.Add(Mismatch(field, description));
        return false;
    }

    private static MetadataValidationIssue Mismatch(ResourceTypeFieldInfo field, string expected) =>
        Issue(field, $"must be {expected}");

    private static MetadataValidationIssue Issue(ResourceTypeFieldInfo field, string problem) => new()
    {
        FieldKey = field.Key,
        Message = $"'{field.Label}' {problem}",
    };
}

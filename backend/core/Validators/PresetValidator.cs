using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Constants;
using Api.Models;
using Api.Models.Preset;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Validates a preset file for schema compliance and business rules: the envelope, the criteria,
/// the space groups and the templates, with every limit from <see cref="DomainLimits"/> (the same
/// ones the criterion, group and template validators apply). A preset is checked as a whole — a
/// template item refers to a criterion key in the same file — so the rules add their failures from
/// one custom step, each message naming the entity it is about.
/// </summary>
public partial class PresetValidator : AbstractValidator<Preset>
{
    /// <summary>Supported preset schema versions.</summary>
    public static readonly string[] SupportedVersions = { "1.0.0" };

    /// <summary>Current/latest schema version for exports.</summary>
    public const string CurrentVersion = "1.0.0";

    public PresetValidator()
    {
        RuleFor(x => x).Custom((preset, ctx) =>
        {
            var errors = new List<string>();
            ValidateEnvelope(preset, errors);
            if (preset.Contents != null)
            {
                ValidateCriteria(preset.Contents.Criteria, errors);
                ValidateSpaceGroups(preset.Contents.SpaceGroups, errors);
                ValidateTemplates(preset.Contents.Templates, preset.Contents.Criteria, errors);
            }
            else
            {
                errors.Add("Preset contents are required");
            }
            foreach (var error in errors)
                ctx.AddFailure(error);
        });
    }

    private static void ValidateEnvelope(Preset preset, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(preset.PresetId))
        {
            errors.Add("PresetId is required");
        }
        else if (!KeyPattern().IsMatch(preset.PresetId))
        {
            errors.Add("PresetId must contain only lowercase letters, numbers, and hyphens (e.g., 'manufacturing-ch-v1')");
        }
        else if (preset.PresetId.Length > DomainLimits.PresetIdMaxLength)
        {
            errors.Add($"PresetId cannot exceed {DomainLimits.PresetIdMaxLength} characters");
        }

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            errors.Add("Name is required");
        }
        else if (preset.Name.Length > DomainLimits.PresetNameMaxLength)
        {
            errors.Add($"Name cannot exceed {DomainLimits.PresetNameMaxLength} characters");
        }

        if (preset.Description?.Length > DomainLimits.PresetDescriptionMaxLength)
        {
            errors.Add($"Description cannot exceed {DomainLimits.PresetDescriptionMaxLength} characters");
        }

        if (string.IsNullOrWhiteSpace(preset.Version))
        {
            errors.Add("Version is required");
        }
        else if (!SupportedVersions.Contains(preset.Version))
        {
            errors.Add($"Unsupported preset version '{preset.Version}'. Supported versions: {string.Join(", ", SupportedVersions)}");
        }

        if (preset.Vendor?.Length > DomainLimits.PresetVendorMaxLength)
        {
            errors.Add($"Vendor cannot exceed {DomainLimits.PresetVendorMaxLength} characters");
        }

        if (preset.Industry?.Length > DomainLimits.PresetIndustryMaxLength)
        {
            errors.Add($"Industry cannot exceed {DomainLimits.PresetIndustryMaxLength} characters");
        }
    }

    private static void ValidateCriteria(List<PresetCriterion> criteria, List<string> errors)
    {
        var keys = new HashSet<string>();

        foreach (var criterion in criteria)
        {
            var prefix = $"Criterion '{criterion.Key}'";

            if (string.IsNullOrWhiteSpace(criterion.Key))
            {
                errors.Add("Criterion key is required");
                continue;
            }

            if (!KeyPattern().IsMatch(criterion.Key))
            {
                errors.Add($"{prefix}: Key must contain only lowercase letters, numbers, and hyphens");
            }

            if (!keys.Add(criterion.Key))
            {
                errors.Add($"{prefix}: Duplicate criterion key");
            }

            if (string.IsNullOrWhiteSpace(criterion.Name))
            {
                errors.Add($"{prefix}: Name is required");
            }
            else if (criterion.Name.Length > DomainLimits.CriterionNameMaxLength)
            {
                errors.Add($"{prefix}: Name cannot exceed {DomainLimits.CriterionNameMaxLength} characters");
            }

            if (criterion.Description?.Length > DomainLimits.CriterionDescriptionMaxLength)
            {
                errors.Add($"{prefix}: Description cannot exceed {DomainLimits.CriterionDescriptionMaxLength} characters");
            }

            if (criterion.DataType == CriterionDataType.Enum)
            {
                if (criterion.EnumValues == null || criterion.EnumValues.Count == 0)
                {
                    errors.Add($"{prefix}: Enum type requires at least one enum value");
                }
                else
                {
                    if (criterion.EnumValues.Any(string.IsNullOrWhiteSpace))
                    {
                        errors.Add($"{prefix}: Enum values cannot be empty");
                    }

                    if (criterion.EnumValues.Distinct().Count() != criterion.EnumValues.Count)
                    {
                        errors.Add($"{prefix}: Enum values must be unique");
                    }
                }
            }

            if (criterion.Unit?.Length > DomainLimits.CriterionUnitMaxLength)
            {
                errors.Add($"{prefix}: Unit cannot exceed {DomainLimits.CriterionUnitMaxLength} characters");
            }
        }
    }

    private static void ValidateSpaceGroups(List<PresetSpaceGroup> groups, List<string> errors)
    {
        var keys = new HashSet<string>();

        foreach (var group in groups)
        {
            var prefix = $"SpaceGroup '{group.Key}'";

            if (string.IsNullOrWhiteSpace(group.Key))
            {
                errors.Add("SpaceGroup key is required");
                continue;
            }

            if (!KeyPattern().IsMatch(group.Key))
            {
                errors.Add($"{prefix}: Key must contain only lowercase letters, numbers, and hyphens");
            }

            if (!keys.Add(group.Key))
            {
                errors.Add($"{prefix}: Duplicate space group key");
            }

            if (string.IsNullOrWhiteSpace(group.Name))
            {
                errors.Add($"{prefix}: Name is required");
            }
            else if (group.Name.Length > DomainLimits.ResourceGroupNameMaxLength)
            {
                errors.Add($"{prefix}: Name cannot exceed {DomainLimits.ResourceGroupNameMaxLength} characters");
            }

            if (group.Description?.Length > DomainLimits.ResourceGroupDescriptionMaxLength)
            {
                errors.Add($"{prefix}: Description cannot exceed {DomainLimits.ResourceGroupDescriptionMaxLength} characters");
            }

            if (!string.IsNullOrWhiteSpace(group.Color) && !HexColorPattern().IsMatch(group.Color))
            {
                errors.Add($"{prefix}: Color must be a valid hex color (#RRGGBB)");
            }
        }
    }

    private static void ValidateTemplates(PresetTemplates templates, List<PresetCriterion> criteria, List<string> errors)
    {
        var criteriaKeys = criteria.Select(c => c.Key).ToHashSet();

        ValidateTemplateList(templates.Space, TemplateEntityTypes.Space, criteriaKeys, errors);
        ValidateTemplateList(templates.Group, TemplateEntityTypes.Group, criteriaKeys, errors);
        ValidateTemplateList(templates.Request, TemplateEntityTypes.Request, criteriaKeys, errors);
    }

    private static void ValidateTemplateList(
        List<PresetTemplate> templates,
        string entityType,
        HashSet<string> criteriaKeys,
        List<string> errors)
    {
        var keys = new HashSet<string>();

        foreach (var template in templates)
        {
            var prefix = $"Template ({entityType}) '{template.Key}'";

            if (string.IsNullOrWhiteSpace(template.Key))
            {
                errors.Add($"Template ({entityType}) key is required");
                continue;
            }

            if (!KeyPattern().IsMatch(template.Key))
            {
                errors.Add($"{prefix}: Key must contain only lowercase letters, numbers, and hyphens");
            }

            if (!keys.Add(template.Key))
            {
                errors.Add($"{prefix}: Duplicate template key");
            }

            if (string.IsNullOrWhiteSpace(template.Name))
            {
                errors.Add($"{prefix}: Name is required");
            }
            else if (template.Name.Length > DomainLimits.TemplateNameMaxLength)
            {
                errors.Add($"{prefix}: Name cannot exceed {DomainLimits.TemplateNameMaxLength} characters");
            }

            if (template.Description?.Length > DomainLimits.TemplateDescriptionMaxLength)
            {
                errors.Add($"{prefix}: Description cannot exceed {DomainLimits.TemplateDescriptionMaxLength} characters");
            }

            if (entityType == TemplateEntityTypes.Request && template.DurationUnit != null)
            {
                var validUnits = new[] { "hours", "days", "weeks" };
                if (!validUnits.Contains(template.DurationUnit.ToLowerInvariant()))
                {
                    errors.Add($"{prefix}: DurationUnit must be one of: {string.Join(", ", validUnits)}");
                }
            }

            var itemCriteriaKeys = new HashSet<string>();
            foreach (var item in template.Items)
            {
                var itemPrefix = $"{prefix} item '{item.CriterionKey}'";

                if (string.IsNullOrWhiteSpace(item.CriterionKey))
                {
                    errors.Add($"{prefix}: Template item criterion key is required");
                    continue;
                }

                if (!criteriaKeys.Contains(item.CriterionKey))
                {
                    errors.Add($"{itemPrefix}: References unknown criterion key '{item.CriterionKey}'");
                }

                if (!itemCriteriaKeys.Add(item.CriterionKey))
                {
                    errors.Add($"{itemPrefix}: Duplicate criterion reference in template");
                }

                if (string.IsNullOrWhiteSpace(item.Value))
                {
                    errors.Add($"{itemPrefix}: Value is required");
                }
                else
                {
                    try
                    {
                        using var _ = JsonDocument.Parse(item.Value);
                    }
                    catch (JsonException)
                    {
                        errors.Add($"{itemPrefix}: Value must be valid JSON");
                    }
                }
            }
        }
    }

    // \A…\z, not ^…$: in .NET `$` also matches before a trailing newline (docs/conventions.md).
    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(ValidationPatterns.HexColor)]
    private static partial Regex HexColorPattern();
}

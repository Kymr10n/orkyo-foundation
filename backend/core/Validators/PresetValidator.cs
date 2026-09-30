using System.Text.Json;
using System.Text.RegularExpressions;
using Api.Constants;
using Api.Models;
using Api.Models.Preset;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Validates a preset file for schema compliance and business rules: the envelope, the resource
/// types, the criteria, the space groups, the templates and the resources, with every limit from
/// <see cref="DomainLimits"/> (the same ones the criterion, group, template and resource validators
/// apply). A preset is checked as a whole — a template item refers to a criterion key in the same
/// file, a resource to a type and to groups of that type — so the rules add their failures from
/// one custom step, each message naming the entity it is about.
/// </summary>
public partial class PresetValidator : AbstractValidator<Preset>
{
    /// <summary>Current/latest preset schema version; what an exported preset declares.</summary>
    public const string CurrentVersion = "1.1.0";

    /// <summary>
    /// Supported preset schema versions. 1.0.0 (criteria, groups, templates) still imports; a
    /// 1.1.0 file (resource types, applicability, typed groups, resources) is refused by an app
    /// that only knows 1.0.0 instead of silently dropping the sections it cannot see.
    /// </summary>
    public static readonly string[] SupportedVersions = { "1.0.0", CurrentVersion };

    // Same limits ResourceTypeRequestValidators applies; the column is VARCHAR(100).
    private const int ResourceTypeKeyMaxLength = 50;
    private const int ResourceTypeDisplayNameMaxLength = 100;
    private const int ResourceTypeIconMaxLength = 50;

    public PresetValidator()
    {
        RuleFor(x => x).Custom((preset, ctx) =>
        {
            var errors = new List<string>();
            ValidateEnvelope(preset, errors);
            if (preset.Contents != null)
            {
                var typeKeys = ValidateResourceTypes(preset.Contents.ResourceTypes, errors);
                ValidateCriteria(preset.Contents.Criteria, typeKeys, errors);
                var groupTypes = ValidateSpaceGroups(preset.Contents.SpaceGroups, typeKeys, errors);
                ValidateTemplates(preset.Contents.Templates, preset.Contents.Criteria, errors);
                ValidateResources(preset.Contents.Resources, typeKeys, groupTypes, preset.Contents.Criteria, errors);
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

    /// <summary>Returns the set of valid type keys, so later sections can check their references.</summary>
    private static HashSet<string> ValidateResourceTypes(List<PresetResourceType> types, List<string> errors)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types)
        {
            var prefix = $"ResourceType '{type.Key}'";

            if (string.IsNullOrWhiteSpace(type.Key))
            {
                errors.Add("ResourceType key is required");
                continue;
            }

            if (!TypeKeyPattern().IsMatch(type.Key))
            {
                errors.Add($"{prefix}: Key must start with a letter and contain only lowercase letters, numbers, and underscores");
            }
            else if (type.Key.Length > ResourceTypeKeyMaxLength)
            {
                errors.Add($"{prefix}: Key cannot exceed {ResourceTypeKeyMaxLength} characters");
            }

            if (!keys.Add(type.Key))
            {
                errors.Add($"{prefix}: Duplicate resource type key");
            }

            // A catalog key is activated from the product's spec: names and flags in the file are
            // ignored, so nothing about them is checked. An ad-hoc key is built from the file.
            if (ResourceTypeCatalog.Find(type.Key) != null) continue;

            if (string.IsNullOrWhiteSpace(type.DisplayName))
            {
                errors.Add($"{prefix}: DisplayName is required for a type that is not in the catalog");
            }
            else if (type.DisplayName.Length > ResourceTypeDisplayNameMaxLength)
            {
                errors.Add($"{prefix}: DisplayName cannot exceed {ResourceTypeDisplayNameMaxLength} characters");
            }

            if (string.IsNullOrWhiteSpace(type.DisplayNamePlural))
            {
                errors.Add($"{prefix}: DisplayNamePlural is required for a type that is not in the catalog");
            }
            else if (type.DisplayNamePlural.Length > ResourceTypeDisplayNameMaxLength)
            {
                errors.Add($"{prefix}: DisplayNamePlural cannot exceed {ResourceTypeDisplayNameMaxLength} characters");
            }

            if (type.Icon?.Length > ResourceTypeIconMaxLength)
            {
                errors.Add($"{prefix}: Icon cannot exceed {ResourceTypeIconMaxLength} characters");
            }
        }

        return keys;
    }

    private static void ValidateCriteria(List<PresetCriterion> criteria, HashSet<string> typeKeys, List<string> errors)
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

            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var typeKey in criterion.ResourceTypeKeys)
            {
                if (!typeKeys.Contains(typeKey))
                {
                    errors.Add($"{prefix}: References unknown resource type key '{typeKey}'");
                }
                if (!seenTypes.Add(typeKey))
                {
                    errors.Add($"{prefix}: Duplicate resource type reference '{typeKey}'");
                }
            }
        }
    }

    /// <summary>Returns group key → type key (null for a 1.0.0 group), for the resource checks.</summary>
    private static Dictionary<string, string?> ValidateSpaceGroups(
        List<PresetSpaceGroup> groups, HashSet<string> typeKeys, List<string> errors)
    {
        var groupTypes = new Dictionary<string, string?>(StringComparer.Ordinal);

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

            if (!groupTypes.TryAdd(group.Key, group.ResourceTypeKey))
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

            if (group.ResourceTypeKey != null && !typeKeys.Contains(group.ResourceTypeKey))
            {
                errors.Add($"{prefix}: References unknown resource type key '{group.ResourceTypeKey}'");
            }
        }

        return groupTypes;
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

                ValidateJsonValue(itemPrefix, item.Value, errors);
            }
        }
    }

    private static void ValidateResources(
        List<PresetResource> resources,
        HashSet<string> typeKeys,
        Dictionary<string, string?> groupTypes,
        List<PresetCriterion> criteria,
        List<string> errors)
    {
        var criteriaKeys = criteria.Select(c => c.Key).ToHashSet();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var codesPerType = new HashSet<(string Type, string Code)>();

        foreach (var resource in resources)
        {
            var prefix = $"Resource '{resource.Key}'";

            if (string.IsNullOrWhiteSpace(resource.Key))
            {
                errors.Add("Resource key is required");
                continue;
            }

            if (!KeyPattern().IsMatch(resource.Key))
            {
                errors.Add($"{prefix}: Key must contain only lowercase letters, numbers, and hyphens");
            }

            if (!keys.Add(resource.Key))
            {
                errors.Add($"{prefix}: Duplicate resource key");
            }

            if (string.IsNullOrWhiteSpace(resource.Name))
            {
                errors.Add($"{prefix}: Name is required");
            }
            else if (resource.Name.Length > DomainLimits.ResourceNameMaxLength)
            {
                errors.Add($"{prefix}: Name cannot exceed {DomainLimits.ResourceNameMaxLength} characters");
            }

            if (resource.Code != null)
            {
                if (resource.Code.Length > DomainLimits.ResourceCodeMaxLength)
                {
                    errors.Add($"{prefix}: Code cannot exceed {DomainLimits.ResourceCodeMaxLength} characters");
                }
                if (!codesPerType.Add((resource.TypeKey ?? "", resource.Code)))
                {
                    errors.Add($"{prefix}: Duplicate code '{resource.Code}' for resource type '{resource.TypeKey}'");
                }
            }

            var typeKnown = !string.IsNullOrWhiteSpace(resource.TypeKey) && typeKeys.Contains(resource.TypeKey);
            if (!typeKnown)
            {
                errors.Add($"{prefix}: References unknown resource type key '{resource.TypeKey}'");
            }

            if (resource.AllocationMode != null && !AllocationModes.All.Contains(resource.AllocationMode))
            {
                errors.Add($"{prefix}: AllocationMode must be one of: {string.Join(", ", AllocationModes.All)}");
            }

            foreach (var groupKey in resource.GroupKeys)
            {
                if (!groupTypes.TryGetValue(groupKey, out var groupType))
                {
                    errors.Add($"{prefix}: References unknown space group key '{groupKey}'");
                }
                else if (typeKnown && groupType != resource.TypeKey)
                {
                    // The member row's composite FKs need the group and the resource to share a
                    // type; a mismatch would fail at the database with a constraint name.
                    errors.Add($"{prefix}: Group '{groupKey}' holds type '{groupType ?? "(placeable)"}', not '{resource.TypeKey}'");
                }
            }

            var seenCriteria = new HashSet<string>(StringComparer.Ordinal);
            foreach (var capability in resource.Capabilities)
            {
                var capPrefix = $"{prefix} capability '{capability.CriterionKey}'";

                if (string.IsNullOrWhiteSpace(capability.CriterionKey))
                {
                    errors.Add($"{prefix}: Capability criterion key is required");
                    continue;
                }

                if (!criteriaKeys.Contains(capability.CriterionKey))
                {
                    errors.Add($"{capPrefix}: References unknown criterion key '{capability.CriterionKey}'");
                }

                if (!seenCriteria.Add(capability.CriterionKey))
                {
                    errors.Add($"{capPrefix}: Duplicate criterion reference on resource");
                }

                ValidateJsonValue(capPrefix, capability.Value, errors);
            }
        }
    }

    /// <summary>A template item and a capability carry the same kind of value; one rule for both.</summary>
    private static void ValidateJsonValue(string prefix, string value, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{prefix}: Value is required");
            return;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException)
        {
            errors.Add($"{prefix}: Value must be valid JSON");
        }
    }

    // \A…\z, not ^…$: in .NET `$` also matches before a trailing newline (docs/conventions.md).
    [GeneratedRegex(@"\A[a-z0-9]+(-[a-z0-9]+)*\z")]
    private static partial Regex KeyPattern();

    // The catalog's spelling (`assembly_station`) and the resource_custom_fields key format.
    [GeneratedRegex(@"\A[a-z][a-z0-9_]*\z")]
    private static partial Regex TypeKeyPattern();

    [GeneratedRegex(ValidationPatterns.HexColor)]
    private static partial Regex HexColorPattern();
}

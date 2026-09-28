using System.Text.Json;
using Api.Models;
using Api.Models.Preset;
using Api.Validators;

namespace Api.Tests.Models;

public class PresetValidatorTests
{
    private static PresetValidationResult Validate(Preset preset)
    {
        var result = new PresetValidator().Validate(preset);
        return new PresetValidationResult(result.IsValid, result.Errors.Select(e => e.ErrorMessage).ToList());
    }

    [Fact]
    public void AKeyWithATrailingNewline_IsRejected()
    {
        // `$` also matches before a trailing newline; the pattern is anchored with \z.
        var preset = CreateValidPreset() with { PresetId = "manufacturing-ch-v1\n" };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("PresetId must contain only lowercase"));
    }

    [Fact]
    public void AGroupNameOverTheGroupLimit_IsRejected()
    {
        var preset = CreateValidPreset();
        preset.Contents!.SpaceGroups[0] = preset.Contents.SpaceGroups[0] with { Name = new string('g', Api.Constants.DomainLimits.ResourceGroupNameMaxLength + 1) };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains($"Name cannot exceed {Api.Constants.DomainLimits.ResourceGroupNameMaxLength} characters"));
    }

    private static Preset CreateValidPreset() => new()
    {
        PresetId = "manufacturing-ch-v1",
        Name = "Manufacturing Switzerland",
        Version = PresetValidator.CurrentVersion,
        CreatedAt = DateTime.UtcNow,
        Contents = new PresetContents
        {
            Criteria =
            [
                new PresetCriterion
                {
                    Key = "shift-model",
                    Name = "Shift Model",
                    DataType = CriterionDataType.Enum,
                    EnumValues = ["two-shift", "three-shift"]
                }
            ],
            SpaceGroups =
            [
                new PresetSpaceGroup
                {
                    Key = "production-hall",
                    Name = "Production Hall",
                    Color = "#AABBCC"
                }
            ],
            Templates = new PresetTemplates
            {
                Request =
                [
                    new PresetTemplate
                    {
                        Key = "work-order",
                        Name = "Work Order",
                        DurationUnit = "hours",
                        Items =
                        [
                            new PresetTemplateItem
                            {
                                CriterionKey = "shift-model",
                                Value = JsonSerializer.Serialize("two-shift")
                            }
                        ]
                    }
                ]
            }
        }
    };

    [Fact]
    public void Validate_ShouldReturnSuccess_ForValidPreset()
    {
        var result = Validate(CreateValidPreset());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ShouldFail_ForUnsupportedVersion()
    {
        var preset = CreateValidPreset() with { Version = "9.9.9" };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Unsupported preset version '9.9.9'"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenEnumCriterionHasNoValues()
    {
        var preset = CreateValidPreset();
        preset.Contents.Criteria[0] = preset.Contents.Criteria[0] with { EnumValues = [] };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Enum type requires at least one enum value"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenSpaceGroupColorIsInvalid()
    {
        var preset = CreateValidPreset();
        preset.Contents.SpaceGroups[0] = preset.Contents.SpaceGroups[0] with { Color = "blue" };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Color must be a valid hex color"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenRequestTemplateUsesUnknownCriterionKey()
    {
        var preset = CreateValidPreset();
        var template = preset.Contents.Templates.Request[0];
        template.Items[0] = template.Items[0] with { CriterionKey = "unknown-key" };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("References unknown criterion key 'unknown-key'"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenTemplateItemValueIsInvalidJson()
    {
        var preset = CreateValidPreset();
        var template = preset.Contents.Templates.Request[0];
        template.Items[0] = template.Items[0] with { Value = "{not-json}" };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("Value must be valid JSON"));
    }

    [Fact]
    public void Validate_ShouldFail_WhenRequestTemplateUsesUnsupportedDurationUnit()
    {
        var preset = CreateValidPreset();
        preset.Contents.Templates.Request[0] = preset.Contents.Templates.Request[0] with { DurationUnit = "months" };

        var result = Validate(preset);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("DurationUnit must be one of: hours, days, weeks"));
    }
}

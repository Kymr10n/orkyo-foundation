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

    // ── 1.1.0: resource types, applicability, typed groups, resources ─────────

    [Fact]
    public void AVersion100File_IsStillAccepted()
    {
        // The sections a 1.0.0 file cannot contain default to empty; nothing else changed for it.
        Validate(CreateValidPreset() with { Version = "1.0.0" }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void AWorkshopPreset_IsValid()
    {
        Validate(CreateWorkshopPreset()).Errors.Should().BeEmpty();
    }

    [Fact]
    public void ACatalogTypeKey_NeedsNoDisplayNames()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.ResourceTypes[1] = new PresetResourceType { Key = "person" };

        Validate(preset).Errors.Should().BeEmpty();
    }

    [Fact]
    public void AnAdHocTypeWithoutDisplayNames_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.ResourceTypes[0] = preset.Contents.ResourceTypes[0] with { DisplayName = null, DisplayNamePlural = null };

        var errors = Validate(preset).Errors;

        errors.Should().Contain(e => e.Contains("DisplayName is required for a type that is not in the catalog"));
        errors.Should().Contain(e => e.Contains("DisplayNamePlural is required for a type that is not in the catalog"));
    }

    [Theory]
    [InlineData("Room")]
    [InlineData("assembly-station")]
    [InlineData("1room")]
    public void ATypeKeyOutsideTheCatalogSpelling_IsRejected(string key)
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.ResourceTypes[0] = preset.Contents.ResourceTypes[0] with { Key = key };
        preset.Contents.Criteria[0] = preset.Contents.Criteria[0] with { ResourceTypeKeys = [key] };
        preset.Contents.SpaceGroups[0] = preset.Contents.SpaceGroups[0] with { ResourceTypeKey = key };
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { TypeKey = key };

        Validate(preset).Errors.Should().Contain(e => e.Contains("Key must start with a letter and contain only lowercase letters, numbers, and underscores"));
    }

    [Fact]
    public void ACriterionApplicableToAnUnknownType_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Criteria[0] = preset.Contents.Criteria[0] with { ResourceTypeKeys = ["room", "hangar"] };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("Criterion 'clean-room': References unknown resource type key 'hangar'"));
    }

    [Fact]
    public void AGroupOfAnUnknownType_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.SpaceGroups[0] = preset.Contents.SpaceGroups[0] with { ResourceTypeKey = "hangar" };

        Validate(preset).Errors.Should().Contain(e => e.Contains("SpaceGroup 'production': References unknown resource type key 'hangar'"));
    }

    [Fact]
    public void AResourceInAGroupOfAnotherType_IsRejected()
    {
        // The member row's composite FKs would refuse it with a constraint name; say it here.
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[1] = preset.Contents.Resources[1] with { GroupKeys = ["production"] };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("Group 'production' holds type 'room', not 'person'"));
    }

    [Fact]
    public void AResourceOfAnUnknownType_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { TypeKey = "hangar" };

        Validate(preset).Errors.Should().Contain(e => e.Contains("Resource 'machine-hall': References unknown resource type key 'hangar'"));
    }

    [Fact]
    public void AResourceWithAnUnknownAllocationMode_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { AllocationMode = "ConcurrentCapacity" };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("AllocationMode must be one of: Exclusive, Fractional"));
    }

    [Fact]
    public void TwoResourcesOfOneTypeWithTheSameCode_AreRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources.Add(preset.Contents.Resources[0] with { Key = "machine-hall-2", Name = "Machine Hall 2" });

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("Duplicate code 'HALL-1' for resource type 'room'"));
    }

    [Fact]
    public void ACapabilityOnAnUnknownCriterion_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with
        {
            Capabilities = [new PresetCapability { CriterionKey = "nope", Value = "true" }]
        };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("capability 'nope': References unknown criterion key 'nope'"));
    }

    [Fact]
    public void ACapabilityWithInvalidJson_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with
        {
            Capabilities = [new PresetCapability { CriterionKey = "clean-room", Value = "{nope" }]
        };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("capability 'clean-room': Value must be valid JSON"));
    }

    [Fact]
    public void AResourceTypeWithoutAKey_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.ResourceTypes.Add(new PresetResourceType { Key = " " });

        Validate(preset).Errors.Should().Contain("ResourceType key is required");
    }

    [Fact]
    public void ADuplicateTypeKey_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.ResourceTypes.Add(preset.Contents.ResourceTypes[0]);

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("ResourceType 'room': Duplicate resource type key"));
    }

    [Fact]
    public void ATypeKeyOrNamesOverTheColumnLimits_AreRejected()
    {
        var preset = CreateWorkshopPreset();
        var longKey = "a" + new string('b', 50);
        preset.Contents.ResourceTypes[0] = preset.Contents.ResourceTypes[0] with
        {
            Key = longKey,
            DisplayName = new string('n', 101),
            DisplayNamePlural = new string('p', 101),
            Icon = new string('i', 51),
        };
        preset.Contents.Criteria[0] = preset.Contents.Criteria[0] with { ResourceTypeKeys = [longKey] };
        preset.Contents.SpaceGroups[0] = preset.Contents.SpaceGroups[0] with { ResourceTypeKey = longKey };
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { TypeKey = longKey };

        var errors = Validate(preset).Errors;

        errors.Should().Contain(e => e.Contains("Key cannot exceed 50 characters"));
        errors.Should().Contain(e => e.Contains("DisplayName cannot exceed 100 characters"));
        errors.Should().Contain(e => e.Contains("DisplayNamePlural cannot exceed 100 characters"));
        errors.Should().Contain(e => e.Contains("Icon cannot exceed 50 characters"));
    }

    [Fact]
    public void ACriterionNamingATypeTwice_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Criteria[0] = preset.Contents.Criteria[0] with { ResourceTypeKeys = ["room", "room"] };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("Criterion 'clean-room': Duplicate resource type reference 'room'"));
    }

    [Fact]
    public void AResourceWithoutAKeyOrName_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources.Add(new PresetResource { Key = "", Name = "x", TypeKey = "room" });
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { Name = " " };

        var errors = Validate(preset).Errors;

        errors.Should().Contain("Resource key is required");
        errors.Should().Contain(e => e.Contains("Resource 'machine-hall': Name is required"));
    }

    [Fact]
    public void AResourceNameOrCodeOverTheLimit_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with
        {
            Name = new string('n', Api.Constants.DomainLimits.ResourceNameMaxLength + 1),
            Code = new string('c', Api.Constants.DomainLimits.ResourceCodeMaxLength + 1),
        };

        var errors = Validate(preset).Errors;

        errors.Should().Contain(e => e.Contains($"Name cannot exceed {Api.Constants.DomainLimits.ResourceNameMaxLength} characters"));
        errors.Should().Contain(e => e.Contains($"Code cannot exceed {Api.Constants.DomainLimits.ResourceCodeMaxLength} characters"));
    }

    [Fact]
    public void AResourceInAnUnknownGroup_IsRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with { GroupKeys = ["nope"] };

        Validate(preset).Errors.Should().ContainSingle(e => e.Contains("Resource 'machine-hall': References unknown space group key 'nope'"));
    }

    [Fact]
    public void ADuplicateResourceKey_AndADuplicateCapability_AreRejected()
    {
        var preset = CreateWorkshopPreset();
        preset.Contents.Resources.Add(preset.Contents.Resources[1] with { Code = "P-002" });
        preset.Contents.Resources[0] = preset.Contents.Resources[0] with
        {
            Capabilities =
            [
                new PresetCapability { CriterionKey = "clean-room", Value = "true" },
                new PresetCapability { CriterionKey = "clean-room", Value = "false" },
                new PresetCapability { CriterionKey = " ", Value = "true" },
            ]
        };

        var errors = Validate(preset).Errors;

        errors.Should().Contain(e => e.Contains("Resource 'anna': Duplicate resource key"));
        errors.Should().Contain(e => e.Contains("capability 'clean-room': Duplicate criterion reference on resource"));
        errors.Should().Contain(e => e.Contains("Resource 'machine-hall': Capability criterion key is required"));
    }

    /// <summary>A minimal 1.1.0 preset: one ad-hoc type, one catalog type, a typed group, two resources.</summary>
    private static Preset CreateWorkshopPreset() => new()
    {
        PresetId = "workshop-v1",
        Name = "Workshop",
        Version = PresetValidator.CurrentVersion,
        CreatedAt = DateTime.UtcNow,
        Contents = new PresetContents
        {
            ResourceTypes =
            [
                new PresetResourceType { Key = "room", DisplayName = "Room", DisplayNamePlural = "Rooms", HasGeometry = true, SingleGroupMembership = true },
                new PresetResourceType { Key = "person" }
            ],
            Criteria =
            [
                new PresetCriterion { Key = "clean-room", Name = "Clean Room", DataType = CriterionDataType.Boolean, ResourceTypeKeys = ["room"] }
            ],
            SpaceGroups =
            [
                new PresetSpaceGroup { Key = "production", Name = "Production", ResourceTypeKey = "room" }
            ],
            Resources =
            [
                new PresetResource
                {
                    Key = "machine-hall", Name = "Machine Hall", Code = "HALL-1", TypeKey = "room", GroupKeys = ["production"],
                    Capabilities = [new PresetCapability { CriterionKey = "clean-room", Value = "false" }]
                },
                new PresetResource { Key = "anna", Name = "Anna Keller", Code = "P-001", TypeKey = "person" }
            ]
        }
    };
}

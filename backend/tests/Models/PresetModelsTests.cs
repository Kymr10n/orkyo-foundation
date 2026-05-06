using Api.Models.Preset;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the uncovered Preset model types: PresetApplication, PresetMapping,
/// PresetEntityType constants, and PresetValidationResult static factory methods.
/// </summary>
public class PresetModelsTests
{
    // ── PresetApplication ──────────────────────────────────────────────────

    [Fact]
    public void PresetApplication_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var application = new PresetApplication
        {
            Id = id,
            PresetId = "manufacturing-ch-v1",
            PresetVersion = "1.0.0",
            AppliedAt = now,
            UpdatedAt = now,
            AppliedByUserId = userId
        };

        application.Id.Should().Be(id);
        application.PresetId.Should().Be("manufacturing-ch-v1");
        application.PresetVersion.Should().Be("1.0.0");
        application.AppliedByUserId.Should().Be(userId);
    }

    [Fact]
    public void PresetApplication_OptionalFields_AreNullByDefault()
    {
        var application = new PresetApplication
        {
            PresetId = "starter-v1",
            PresetVersion = "0.1.0"
        };

        application.UpdatedAt.Should().BeNull();
        application.AppliedByUserId.Should().BeNull();
    }

    // ── PresetMapping ──────────────────────────────────────────────────────

    [Fact]
    public void PresetMapping_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var applicationId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var mapping = new PresetMapping
        {
            Id = id,
            PresetApplicationId = applicationId,
            EntityType = PresetEntityType.Criterion,
            LogicalKey = "shift-model",
            EntityId = entityId,
            CreatedAt = now
        };

        mapping.Id.Should().Be(id);
        mapping.PresetApplicationId.Should().Be(applicationId);
        mapping.EntityType.Should().Be("criterion");
        mapping.LogicalKey.Should().Be("shift-model");
        mapping.EntityId.Should().Be(entityId);
    }

    // ── PresetEntityType constants ─────────────────────────────────────────

    [Fact]
    public void PresetEntityType_Constants_HaveExpectedValues()
    {
        PresetEntityType.Criterion.Should().Be("criterion");
        PresetEntityType.SpaceGroup.Should().Be("space_group");
        PresetEntityType.TemplateSpace.Should().Be("template_space");
        PresetEntityType.TemplateGroup.Should().Be("template_group");
        PresetEntityType.TemplateRequest.Should().Be("template_request");
        PresetEntityType.TemplateItem.Should().Be("template_item");
    }

    // ── PresetValidationResult static factories ────────────────────────────

    [Fact]
    public void PresetValidationResult_Success_IsValidWithEmptyErrors()
    {
        var result = PresetValidationResult.Success();

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void PresetValidationResult_Failure_SingleError_IsInvalidWithOneError()
    {
        var result = PresetValidationResult.Failure("Criterion 'weight' is missing a name.");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Should().Contain("weight");
    }

    [Fact]
    public void PresetValidationResult_Failure_MultipleErrors_IncludesAll()
    {
        var errors = new List<string>
        {
            "Criterion 'c1' is missing a name.",
            "Template 't1' has no items.",
            "SpaceGroup 'sg1' has invalid color."
        };

        var result = PresetValidationResult.Failure(errors);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(3);
    }
}

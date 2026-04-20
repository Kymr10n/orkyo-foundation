using Api.Models;
using Xunit;

namespace Api.Tests.Models;

public class TemplateTests
{
    [Fact]
    public void Template_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var template = new Template();

        // Assert
        Assert.Equal(Guid.Empty, template.Id);
        Assert.Equal(string.Empty, template.Name);
        Assert.Null(template.Description);
        Assert.Equal(string.Empty, template.EntityType);
        Assert.Null(template.DurationValue);
        Assert.Null(template.DurationUnit);
        Assert.False(template.FixedStart);
        Assert.False(template.FixedEnd);
        Assert.True(template.FixedDuration);
        Assert.Equal(DateTime.MinValue, template.CreatedAt);
        Assert.Equal(DateTime.MinValue, template.UpdatedAt);
    }

    [Fact]
    public void Template_ShouldAllowSettingAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var name = "Test Template";
        var description = "Test Description";
        var entityType = "request";
        var durationValue = 60;
        var durationUnit = "minutes";
        var fixedStart = true;
        var fixedEnd = true;
        var fixedDuration = false;
        var createdAt = DateTime.UtcNow;
        var updatedAt = DateTime.UtcNow;

        // Act
        var template = new Template
        {
            Id = id,
            Name = name,
            Description = description,
            EntityType = entityType,
            DurationValue = durationValue,
            DurationUnit = durationUnit,
            FixedStart = fixedStart,
            FixedEnd = fixedEnd,
            FixedDuration = fixedDuration,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        // Assert
        Assert.Equal(id, template.Id);
        Assert.Equal(name, template.Name);
        Assert.Equal(description, template.Description);
        Assert.Equal(entityType, template.EntityType);
        Assert.Equal(durationValue, template.DurationValue);
        Assert.Equal(durationUnit, template.DurationUnit);
        Assert.Equal(fixedStart, template.FixedStart);
        Assert.Equal(fixedEnd, template.FixedEnd);
        Assert.Equal(fixedDuration, template.FixedDuration);
        Assert.Equal(createdAt, template.CreatedAt);
        Assert.Equal(updatedAt, template.UpdatedAt);
    }

    [Fact]
    public void TemplateItem_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var templateItem = new TemplateItem();

        // Assert
        Assert.Equal(Guid.Empty, templateItem.Id);
        Assert.Equal(Guid.Empty, templateItem.TemplateId);
        Assert.Equal(Guid.Empty, templateItem.CriterionId);
        Assert.Equal("{}", templateItem.Value);
        Assert.Equal(DateTime.MinValue, templateItem.CreatedAt);
        Assert.Equal(DateTime.MinValue, templateItem.UpdatedAt);
        Assert.Null(templateItem.CriterionName);
        Assert.Null(templateItem.CriterionDataType);
        Assert.Null(templateItem.CriterionCategory);
    }

    [Fact]
    public void TemplateItem_ShouldAllowSettingAllProperties()
    {
        // Arrange
        var id = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var criterionId = Guid.NewGuid();
        var value = "{\"key\": \"value\"}";
        var createdAt = DateTime.UtcNow;
        var updatedAt = DateTime.UtcNow;
        var criterionName = "Test Criterion";
        var criterionDataType = "text";
        var criterionCategory = "general";

        // Act
        var templateItem = new TemplateItem
        {
            Id = id,
            TemplateId = templateId,
            CriterionId = criterionId,
            Value = value,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            CriterionName = criterionName,
            CriterionDataType = criterionDataType,
            CriterionCategory = criterionCategory
        };

        // Assert
        Assert.Equal(id, templateItem.Id);
        Assert.Equal(templateId, templateItem.TemplateId);
        Assert.Equal(criterionId, templateItem.CriterionId);
        Assert.Equal(value, templateItem.Value);
        Assert.Equal(createdAt, templateItem.CreatedAt);
        Assert.Equal(updatedAt, templateItem.UpdatedAt);
        Assert.Equal(criterionName, templateItem.CriterionName);
        Assert.Equal(criterionDataType, templateItem.CriterionDataType);
        Assert.Equal(criterionCategory, templateItem.CriterionCategory);
    }

    [Fact]
    public void CreateTemplateRequest_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var request = new CreateTemplateRequest();

        // Assert
        Assert.Equal(string.Empty, request.Name);
        Assert.Null(request.Description);
        Assert.Equal(string.Empty, request.EntityType);
        Assert.Null(request.DurationValue);
        Assert.Null(request.DurationUnit);
        Assert.False(request.FixedStart);
        Assert.False(request.FixedEnd);
        Assert.True(request.FixedDuration);
    }

    [Theory]
    [InlineData("request", 60, "minutes", true, false, true)]
    [InlineData("space", null, null, false, false, false)]
    [InlineData("group", 120, "hours", true, true, false)]
    public void CreateTemplateRequest_ShouldSupportVariousConfigurations(
        string entityType,
        int? durationValue,
        string? durationUnit,
        bool fixedStart,
        bool fixedEnd,
        bool fixedDuration)
    {
        // Act
        var request = new CreateTemplateRequest
        {
            Name = "Test Template",
            Description = "Test Description",
            EntityType = entityType,
            DurationValue = durationValue,
            DurationUnit = durationUnit,
            FixedStart = fixedStart,
            FixedEnd = fixedEnd,
            FixedDuration = fixedDuration
        };

        // Assert
        Assert.Equal("Test Template", request.Name);
        Assert.Equal("Test Description", request.Description);
        Assert.Equal(entityType, request.EntityType);
        Assert.Equal(durationValue, request.DurationValue);
        Assert.Equal(durationUnit, request.DurationUnit);
        Assert.Equal(fixedStart, request.FixedStart);
        Assert.Equal(fixedEnd, request.FixedEnd);
        Assert.Equal(fixedDuration, request.FixedDuration);
    }

    [Fact]
    public void UpdateTemplateRequest_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var request = new UpdateTemplateRequest();

        // Assert
        Assert.Equal(string.Empty, request.Name);
        Assert.Null(request.Description);
        Assert.Equal(string.Empty, request.EntityType);
        Assert.Null(request.DurationValue);
        Assert.Null(request.DurationUnit);
        Assert.False(request.FixedStart);
        Assert.False(request.FixedEnd);
        Assert.True(request.FixedDuration);
    }

    [Fact]
    public void UpdateTemplateRequest_ShouldAllowSettingAllProperties()
    {
        // Arrange
        var name = "Updated Template";
        var description = "Updated Description";
        var entityType = "space";
        var durationValue = 90;
        var durationUnit = "minutes";
        var fixedStart = true;
        var fixedEnd = false;
        var fixedDuration = true;

        // Act
        var request = new UpdateTemplateRequest
        {
            Name = name,
            Description = description,
            EntityType = entityType,
            DurationValue = durationValue,
            DurationUnit = durationUnit,
            FixedStart = fixedStart,
            FixedEnd = fixedEnd,
            FixedDuration = fixedDuration
        };

        // Assert
        Assert.Equal(name, request.Name);
        Assert.Equal(description, request.Description);
        Assert.Equal(entityType, request.EntityType);
        Assert.Equal(durationValue, request.DurationValue);
        Assert.Equal(durationUnit, request.DurationUnit);
        Assert.Equal(fixedStart, request.FixedStart);
        Assert.Equal(fixedEnd, request.FixedEnd);
        Assert.Equal(fixedDuration, request.FixedDuration);
    }

    [Fact]
    public void CreateTemplateItemRequest_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var request = new CreateTemplateItemRequest();

        // Assert
        Assert.Equal(Guid.Empty, request.CriterionId);
        Assert.Equal("{}", request.Value);
    }

    [Fact]
    public void CreateTemplateItemRequest_ShouldAllowSettingProperties()
    {
        // Arrange
        var criterionId = Guid.NewGuid();
        var value = "{\"key\": \"value\", \"number\": 42}";

        // Act
        var request = new CreateTemplateItemRequest
        {
            CriterionId = criterionId,
            Value = value
        };

        // Assert
        Assert.Equal(criterionId, request.CriterionId);
        Assert.Equal(value, request.Value);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"simple\": \"value\"}")]
    [InlineData("{\"complex\": {\"nested\": \"value\", \"array\": [1, 2, 3]}}")]
    [InlineData("{\"boolean\": true, \"number\": 123, \"string\": \"test\"}")]
    public void TemplateItem_ValueProperty_ShouldAcceptValidJsonStrings(string jsonValue)
    {
        // Act
        var templateItem = new TemplateItem
        {
            Value = jsonValue
        };

        // Assert
        Assert.Equal(jsonValue, templateItem.Value);
    }

    [Fact]
    public void Template_RequestSpecificFields_ShouldOnlyApplyToRequestEntityType()
    {
        // This test documents the business rule that duration fields are only relevant for request templates

        // Arrange & Act
        var requestTemplate = new Template
        {
            EntityType = "request",
            DurationValue = 60,
            DurationUnit = "minutes",
            FixedStart = true,
            FixedEnd = false,
            FixedDuration = true
        };

        var spaceTemplate = new Template
        {
            EntityType = "space",
            // Duration fields can be set but are not used for space/group templates
            DurationValue = null,
            DurationUnit = null,
            FixedStart = false,
            FixedEnd = false,
            FixedDuration = true // default value
        };

        // Assert
        Assert.Equal("request", requestTemplate.EntityType);
        Assert.Equal(60, requestTemplate.DurationValue);
        Assert.Equal("minutes", requestTemplate.DurationUnit);

        Assert.Equal("space", spaceTemplate.EntityType);
        Assert.Null(spaceTemplate.DurationValue);
        Assert.Null(spaceTemplate.DurationUnit);
    }
}

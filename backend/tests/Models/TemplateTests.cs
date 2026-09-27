using Api.Models;

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
    public void CreateTemplateItemRequest_ShouldHaveCorrectDefaultValues()
    {
        // Act
        var request = new CreateTemplateItemRequest();

        // Assert
        Assert.Equal(Guid.Empty, request.CriterionId);
        Assert.Equal("{}", request.Value);
    }
}

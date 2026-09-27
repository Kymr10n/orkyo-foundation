using Api.Models;

namespace Api.Tests.Models;

public class SchedulingModelsTests
{
    [Fact]
    public void UpsertSchedulingSettingsRequest_ShouldHaveDefaults()
    {
        var request = new UpsertSchedulingSettingsRequest();

        request.TimeZone.Should().Be("UTC");
        request.WorkingHoursEnabled.Should().BeFalse();
        request.WorkingDayStart.Should().Be("08:00");
        request.WorkingDayEnd.Should().Be("17:00");
        request.WeekendsEnabled.Should().BeTrue();
        request.PublicHolidaysEnabled.Should().BeFalse();
        request.PublicHolidayRegion.Should().BeNull();
    }

    [Fact]
    public void CreateResourceAbsenceRequest_ShouldHaveDefaults()
    {
        var request = new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Vacation,
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };

        request.IsRecurring.Should().BeFalse();
        request.Enabled.Should().BeTrue();
        request.Notes.Should().BeNull();
        request.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public void ResourceAbsenceInfo_ScopesEmptyByDefault()
    {
        var info = new ResourceAbsenceInfo
        {
            Id = Guid.NewGuid(),
            ResourceId = Guid.NewGuid(),
            AbsenceType = AbsenceType.Custom,
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = false,
            Enabled = true
        };

        info.Notes.Should().BeNull();
        info.RecurrenceRule.Should().BeNull();
    }

    [Fact]
    public void RequestInfo_SchedulingSettingsApply_DefaultsToTrue()
    {
        // Verify the field exists and the CreateRequestRequest defaults it to true
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours
        };

        request.SchedulingSettingsApply.Should().BeTrue();
    }

    [Fact]
    public void SchedulingSettingsInfo_Default_ReturnsExpectedValues()
    {
        var siteId = Guid.NewGuid();

        var defaults = SchedulingSettingsInfo.Default(siteId);

        defaults.Id.Should().Be(Guid.Empty);
        defaults.SiteId.Should().Be(siteId);
        defaults.TimeZone.Should().Be("UTC");
        defaults.WorkingHoursEnabled.Should().BeFalse();
        defaults.WeekendsEnabled.Should().BeTrue();
        defaults.PublicHolidaysEnabled.Should().BeFalse();
        defaults.WorkingDayStart.Should().Be(new TimeOnly(8, 0));
        defaults.WorkingDayEnd.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public void UpdateResourceAbsenceRequest_AllPropertiesNullByDefault()
    {
        var req = new UpdateResourceAbsenceRequest();

        req.Title.Should().BeNull();
        req.AbsenceType.Should().BeNull();
        req.Notes.Should().BeNull();
        req.StartTs.Should().BeNull();
        req.EndTs.Should().BeNull();
        req.IsRecurring.Should().BeNull();
        req.RecurrenceRule.Should().BeNull();
        req.Enabled.Should().BeNull();
    }
}

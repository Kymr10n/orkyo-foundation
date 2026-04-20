using Api.Models;

namespace Api.Tests.Models;

public class SchedulingModelsTests
{
    [Fact]
    public void SchedulingSettingsInfo_ShouldStoreAllProperties()
    {
        var siteId = Guid.NewGuid();
        var settings = new SchedulingSettingsInfo
        {
            Id = Guid.NewGuid(),
            SiteId = siteId,
            TimeZone = "Europe/Berlin",
            WorkingHoursEnabled = true,
            WorkingDayStart = new TimeOnly(8, 0),
            WorkingDayEnd = new TimeOnly(17, 0),
            WeekendsEnabled = false,
            PublicHolidaysEnabled = true,
            PublicHolidayRegion = "DE"
        };

        settings.SiteId.Should().Be(siteId);
        settings.TimeZone.Should().Be("Europe/Berlin");
        settings.WorkingHoursEnabled.Should().BeTrue();
        settings.WorkingDayStart.Should().Be(new TimeOnly(8, 0));
        settings.WorkingDayEnd.Should().Be(new TimeOnly(17, 0));
        settings.WeekendsEnabled.Should().BeFalse();
        settings.PublicHolidaysEnabled.Should().BeTrue();
        settings.PublicHolidayRegion.Should().Be("DE");
    }

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
    public void OffTimeInfo_ShouldStoreAllProperties()
    {
        var offTime = new OffTimeInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Christmas",
            Type = OffTimeType.Holiday,
            AppliesToAllSpaces = true,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc),
            IsRecurring = true,
            RecurrenceRule = "FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=25",
            Enabled = true
        };

        offTime.Title.Should().Be("Christmas");
        offTime.Type.Should().Be(OffTimeType.Holiday);
        offTime.AppliesToAllSpaces.Should().BeTrue();
        offTime.IsRecurring.Should().BeTrue();
        offTime.RecurrenceRule.Should().NotBeNull();
    }

    [Fact]
    public void CreateOffTimeRequest_ShouldHaveDefaults()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };

        request.Type.Should().Be(OffTimeType.Custom);
        request.AppliesToAllSpaces.Should().BeTrue();
        request.IsRecurring.Should().BeFalse();
        request.Enabled.Should().BeTrue();
    }

    [Fact]
    public void OffTimeType_ShouldHaveThreeValues()
    {
        Enum.GetValues<OffTimeType>().Should().HaveCount(3);
        Enum.GetValues<OffTimeType>().Should().Contain(OffTimeType.Holiday);
        Enum.GetValues<OffTimeType>().Should().Contain(OffTimeType.Maintenance);
        Enum.GetValues<OffTimeType>().Should().Contain(OffTimeType.Custom);
    }

    [Fact]
    public void OffTimeInfo_SpaceIds_NullByDefault()
    {
        var offTime = new OffTimeInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Test",
            Type = OffTimeType.Custom,
            AppliesToAllSpaces = true,
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = false,
            Enabled = true
        };

        offTime.SpaceIds.Should().BeNull();
    }

    [Fact]
    public void OffTimeInfo_WithExpression_ShouldCreateModifiedCopy()
    {
        var original = new OffTimeInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Test",
            Type = OffTimeType.Custom,
            AppliesToAllSpaces = true,
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = false,
            Enabled = true
        };

        var modified = original with { Title = "Modified", Enabled = false };

        modified.Title.Should().Be("Modified");
        modified.Enabled.Should().BeFalse();
        original.Title.Should().Be("Test");
        original.Enabled.Should().BeTrue();
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
}

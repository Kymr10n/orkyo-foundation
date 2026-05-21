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
            AppliesToAllResources = true,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc),
            IsRecurring = true,
            RecurrenceRule = "FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=25",
            Enabled = true
        };

        offTime.Title.Should().Be("Christmas");
        offTime.Type.Should().Be(OffTimeType.Holiday);
        offTime.AppliesToAllResources.Should().BeTrue();
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
        request.AppliesToAllResources.Should().BeTrue();
        request.IsRecurring.Should().BeFalse();
        request.Enabled.Should().BeTrue();
    }

    [Fact]
    public void OffTimeType_ShouldContainSchedulingAndHrValues()
    {
        var values = Enum.GetValues<OffTimeType>();
        // Scheduling types
        values.Should().Contain(OffTimeType.Holiday);
        values.Should().Contain(OffTimeType.Maintenance);
        values.Should().Contain(OffTimeType.Custom);
        // HR absence types
        values.Should().Contain(OffTimeType.Vacation);
        values.Should().Contain(OffTimeType.SickLeave);
        values.Should().Contain(OffTimeType.Unavailable);
        values.Should().Contain(OffTimeType.Training);
    }

    [Fact]
    public void OffTimeInfo_ResourceIds_NullByDefault()
    {
        var offTime = new OffTimeInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Test",
            Type = OffTimeType.Custom,
            AppliesToAllResources = true,
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = false,
            Enabled = true
        };

        offTime.ResourceIds.Should().BeNull();
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
            AppliesToAllResources = true,
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
    public void UpdateOffTimeRequest_AllPropertiesNullByDefault()
    {
        var req = new UpdateOffTimeRequest();

        req.Title.Should().BeNull();
        req.Type.Should().BeNull();
        req.AppliesToAllResources.Should().BeNull();
        req.ResourceIds.Should().BeNull();
        req.StartTs.Should().BeNull();
        req.EndTs.Should().BeNull();
        req.IsRecurring.Should().BeNull();
        req.RecurrenceRule.Should().BeNull();
        req.Enabled.Should().BeNull();
    }

    [Fact]
    public void UpdateOffTimeRequest_StoresProvidedValues()
    {
        var start = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc);

        var req = new UpdateOffTimeRequest
        {
            Title = "Christmas Holiday",
            Type = OffTimeType.Holiday,
            AppliesToAllResources = true,
            StartTs = start,
            EndTs = end,
            IsRecurring = false,
            Enabled = true
        };

        req.Title.Should().Be("Christmas Holiday");
        req.Type.Should().Be(OffTimeType.Holiday);
        req.AppliesToAllResources.Should().BeTrue();
        req.StartTs.Should().Be(start);
    }
}

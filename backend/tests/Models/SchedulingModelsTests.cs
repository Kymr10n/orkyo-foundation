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
    public void AvailabilityEventInfo_ShouldStoreAllProperties()
    {
        var ev = new AvailabilityEventInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Christmas",
            EventType = AvailabilityEventType.PublicHoliday,
            DefaultEffect = DefaultEffect.Closed,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc),
            IsRecurring = true,
            RecurrenceRule = "FREQ=YEARLY;BYMONTH=12;BYMONTHDAY=25",
            Enabled = true
        };

        ev.Title.Should().Be("Christmas");
        ev.EventType.Should().Be(AvailabilityEventType.PublicHoliday);
        ev.DefaultEffect.Should().Be(DefaultEffect.Closed);
        ev.IsRecurring.Should().BeTrue();
        ev.RecurrenceRule.Should().NotBeNull();
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
    public void AbsenceType_ShouldContainExpectedValues()
    {
        var values = Enum.GetValues<AbsenceType>();
        values.Should().Contain(AbsenceType.Vacation);
        values.Should().Contain(AbsenceType.Sickness);
        values.Should().Contain(AbsenceType.Unavailable);
        values.Should().Contain(AbsenceType.Training);
        values.Should().Contain(AbsenceType.Maintenance);
        values.Should().Contain(AbsenceType.Custom);
    }

    [Fact]
    public void AvailabilityEventType_ShouldContainExpectedValues()
    {
        var values = Enum.GetValues<AvailabilityEventType>();
        values.Should().Contain(AvailabilityEventType.PublicHoliday);
        values.Should().Contain(AvailabilityEventType.Shutdown);
        values.Should().Contain(AvailabilityEventType.Maintenance);
        values.Should().Contain(AvailabilityEventType.Custom);
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
    public void AvailabilityEventInfo_WithExpression_ShouldCreateModifiedCopy()
    {
        var original = new AvailabilityEventInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            Title = "Test",
            EventType = AvailabilityEventType.Custom,
            DefaultEffect = DefaultEffect.Closed,
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

    [Fact]
    public void UpdateAvailabilityEventRequest_StoresProvidedValues()
    {
        var start = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc);

        var req = new UpdateAvailabilityEventRequest
        {
            Title = "Christmas Holiday",
            EventType = AvailabilityEventType.PublicHoliday,
            DefaultEffect = DefaultEffect.Closed,
            StartTs = start,
            EndTs = end,
            IsRecurring = false,
            Enabled = true
        };

        req.Title.Should().Be("Christmas Holiday");
        req.EventType.Should().Be(AvailabilityEventType.PublicHoliday);
        req.DefaultEffect.Should().Be(DefaultEffect.Closed);
        req.StartTs.Should().Be(start);
    }
}

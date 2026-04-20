using Api.Models;
using Api.Validators;
using FluentValidation.TestHelper;

namespace Api.Tests.Validators;

public class SchedulingValidatorTests
{
    // ── UpsertSchedulingSettingsRequest ──────────────────────────────

    private readonly UpsertSchedulingSettingsRequestValidator _settingsValidator = new();

    [Fact]
    public void SettingsValidator_ValidDefaults_Passes()
    {
        var request = new UpsertSchedulingSettingsRequest();
        var result = _settingsValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SettingsValidator_ValidFullRequest_Passes()
    {
        var request = new UpsertSchedulingSettingsRequest
        {
            TimeZone = "Europe/Berlin",
            WorkingHoursEnabled = true,
            WorkingDayStart = "09:00",
            WorkingDayEnd = "18:00",
            WeekendsEnabled = false,
            PublicHolidaysEnabled = true,
            PublicHolidayRegion = "DE"
        };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SettingsValidator_InvalidTimeZone_Fails()
    {
        var request = new UpsertSchedulingSettingsRequest { TimeZone = "NotATimezone" };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.TimeZone);
    }

    [Fact]
    public void SettingsValidator_EmptyTimeZone_Fails()
    {
        var request = new UpsertSchedulingSettingsRequest { TimeZone = "" };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.TimeZone);
    }

    [Fact]
    public void SettingsValidator_EndBeforeStart_Fails()
    {
        var request = new UpsertSchedulingSettingsRequest
        {
            WorkingDayStart = "17:00",
            WorkingDayEnd = "08:00"
        };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.WorkingDayEnd);
    }

    [Fact]
    public void SettingsValidator_InvalidTimeFormat_Fails()
    {
        var request = new UpsertSchedulingSettingsRequest { WorkingDayStart = "not-a-time" };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.WorkingDayStart);
    }

    [Fact]
    public void SettingsValidator_PublicHolidaysEnabled_RequiresRegion()
    {
        var request = new UpsertSchedulingSettingsRequest
        {
            PublicHolidaysEnabled = true,
            PublicHolidayRegion = null
        };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.PublicHolidayRegion);
    }

    [Fact]
    public void SettingsValidator_PublicHolidaysDisabled_RegionOptional()
    {
        var request = new UpsertSchedulingSettingsRequest
        {
            PublicHolidaysEnabled = false,
            PublicHolidayRegion = null
        };
        var result = _settingsValidator.TestValidate(request);
        result.ShouldNotHaveValidationErrorFor(x => x.PublicHolidayRegion);
    }

    // ── CreateOffTimeRequest ────────────────────────────────────────

    private readonly CreateOffTimeRequestValidator _createValidator = new();

    [Fact]
    public void CreateOffTime_ValidRequest_Passes()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Christmas",
            Type = OffTimeType.Holiday,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc)
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateOffTime_EmptyTitle_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateOffTime_TitleTooLong_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = new string('a', 201),
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateOffTime_EndBeforeStart_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow.AddHours(1),
            EndTs = DateTime.UtcNow
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndTs);
    }

    [Fact]
    public void CreateOffTime_RecurringWithoutRule_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = true,
            RecurrenceRule = null
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.RecurrenceRule);
    }

    [Fact]
    public void CreateOffTime_NonRecurringWithRule_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = false,
            RecurrenceRule = "FREQ=YEARLY"
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.RecurrenceRule);
    }

    [Fact]
    public void CreateOffTime_NotAllSpaces_RequiresSpaceIds()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            AppliesToAllSpaces = false,
            SpaceIds = null
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.SpaceIds);
    }

    [Fact]
    public void CreateOffTime_NotAllSpaces_EmptySpaceIds_Fails()
    {
        var request = new CreateOffTimeRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            AppliesToAllSpaces = false,
            SpaceIds = new List<Guid>()
        };
        var result = _createValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.SpaceIds);
    }

    // ── UpdateOffTimeRequest ────────────────────────────────────────

    private readonly UpdateOffTimeRequestValidator _updateValidator = new();

    [Fact]
    public void UpdateOffTime_EmptyRequest_Passes()
    {
        var request = new UpdateOffTimeRequest();
        var result = _updateValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateOffTime_ValidPartialUpdate_Passes()
    {
        var request = new UpdateOffTimeRequest { Title = "Updated Title" };
        var result = _updateValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateOffTime_TitleTooLong_Fails()
    {
        var request = new UpdateOffTimeRequest { Title = new string('a', 201) };
        var result = _updateValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }
}

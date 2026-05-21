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

    // ── CreateAvailabilityEventRequest ─────────────────────────────

    private readonly CreateAvailabilityEventRequestValidator _createEventValidator = new();

    [Fact]
    public void CreateAvailabilityEvent_ValidRequest_Passes()
    {
        var request = new CreateAvailabilityEventRequest
        {
            Title = "Christmas",
            EventType = AvailabilityEventType.PublicHoliday,
            DefaultEffect = DefaultEffect.Closed,
            StartTs = new DateTime(2026, 12, 25, 0, 0, 0, DateTimeKind.Utc),
            EndTs = new DateTime(2026, 12, 26, 0, 0, 0, DateTimeKind.Utc)
        };
        var result = _createEventValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateAvailabilityEvent_EmptyTitle_Fails()
    {
        var request = new CreateAvailabilityEventRequest
        {
            Title = "",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };
        var result = _createEventValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateAvailabilityEvent_EndBeforeStart_Fails()
    {
        var request = new CreateAvailabilityEventRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow.AddHours(1),
            EndTs = DateTime.UtcNow
        };
        var result = _createEventValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndTs);
    }

    [Fact]
    public void CreateAvailabilityEvent_RecurringWithoutRule_Fails()
    {
        var request = new CreateAvailabilityEventRequest
        {
            Title = "Test",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1),
            IsRecurring = true,
            RecurrenceRule = null
        };
        var result = _createEventValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.RecurrenceRule);
    }

    // ── CreateResourceAbsenceRequest ───────────────────────────────

    private readonly CreateResourceAbsenceRequestValidator _createAbsenceValidator = new();

    [Fact]
    public void CreateAbsence_ValidRequest_Passes()
    {
        var request = new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Vacation,
            Title = "Holiday",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddDays(5)
        };
        var result = _createAbsenceValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void CreateAbsence_EmptyTitle_Fails()
    {
        var request = new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Custom,
            Title = "",
            StartTs = DateTime.UtcNow,
            EndTs = DateTime.UtcNow.AddHours(1)
        };
        var result = _createAbsenceValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void CreateAbsence_EndBeforeStart_Fails()
    {
        var request = new CreateResourceAbsenceRequest
        {
            AbsenceType = AbsenceType.Sickness,
            Title = "Sick",
            StartTs = DateTime.UtcNow.AddHours(1),
            EndTs = DateTime.UtcNow
        };
        var result = _createAbsenceValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.EndTs);
    }

    // ── UpdateResourceAbsenceRequest ───────────────────────────────

    private readonly UpdateResourceAbsenceRequestValidator _updateAbsenceValidator = new();

    [Fact]
    public void UpdateAbsence_EmptyRequest_Passes()
    {
        var request = new UpdateResourceAbsenceRequest();
        var result = _updateAbsenceValidator.TestValidate(request);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void UpdateAbsence_TitleTooLong_Fails()
    {
        var request = new UpdateResourceAbsenceRequest { Title = new string('a', 201) };
        var result = _updateAbsenceValidator.TestValidate(request);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }
}

using System.Globalization;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpsertSchedulingSettingsRequestValidator : AbstractValidator<UpsertSchedulingSettingsRequest>
{
    private static readonly HashSet<string> ValidTimeZones =
        new(TimeZoneInfo.GetSystemTimeZones().Select(tz => tz.Id), StringComparer.OrdinalIgnoreCase);

    public UpsertSchedulingSettingsRequestValidator()
    {
        RuleFor(x => x.TimeZone)
            .NotEmpty().WithMessage("TimeZone is required")
            .Must(tz => ValidTimeZones.Contains(tz))
            .WithMessage("TimeZone must be a valid IANA time zone identifier");

        RuleFor(x => x.WorkingDayStart)
            .NotEmpty().WithMessage("WorkingDayStart is required")
            .Must(BeValidTime).WithMessage("WorkingDayStart must be a valid time (HH:mm)");

        RuleFor(x => x.WorkingDayEnd)
            .NotEmpty().WithMessage("WorkingDayEnd is required")
            .Must(BeValidTime).WithMessage("WorkingDayEnd must be a valid time (HH:mm)");

        RuleFor(x => x)
            .Must(x => !WorkingTime.TryParse(x.WorkingDayStart, out var start)
                       || !WorkingTime.TryParse(x.WorkingDayEnd, out var end)
                       || start < end)
            .WithName("WorkingDayEnd")
            .WithMessage("WorkingDayEnd must be after WorkingDayStart");

        RuleFor(x => x.PublicHolidayRegion)
            .MaximumLength(10)
            .When(x => x.PublicHolidayRegion != null);

        RuleFor(x => x.PublicHolidayRegion)
            .NotEmpty().When(x => x.PublicHolidaysEnabled)
            .WithMessage("PublicHolidayRegion is required when public holidays are enabled");
    }

    private static bool BeValidTime(string time) => WorkingTime.TryParse(time, out _);
}

/// <summary>
/// A working-day boundary as the settings carry it: exactly <c>HH:mm</c>. <c>TimeSpan.TryParse</c>
/// read "5" as five days, which then failed the <c>time</c> column write with a 500.
/// </summary>
internal static class WorkingTime
{
    public const string Format = "HH:mm";

    public static bool TryParse(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    /// <summary>For a value the validator already accepted.</summary>
    public static TimeOnly Parse(string value) =>
        TimeOnly.ParseExact(value, Format, CultureInfo.InvariantCulture);
}

public class CreateAvailabilityEventRequestValidator : AbstractValidator<CreateAvailabilityEventRequest>
{
    public CreateAvailabilityEventRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.EventType).IsInEnum();
        RuleFor(x => x.DefaultEffect).IsInEnum();
        RuleFor(x => x.StartTs).NotEmpty();
        RuleFor(x => x.EndTs).NotEmpty().GreaterThan(x => x.StartTs).WithMessage("EndTs must be after StartTs");
        SharedRecurrenceRules.Apply(this, x => x.IsRecurring, x => x.RecurrenceRule);
        RuleForEach(x => x.Scopes).SetValidator(new AddScopeRequestValidator());
    }
}

/// <summary>The rules an availability-event update and an absence update share.</summary>
public abstract class RecurringWindowUpdateValidator<T> : AbstractValidator<T> where T : IRecurringWindowUpdate
{
    protected RecurringWindowUpdateValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title != null);
        RuleFor(x => x.EndTs)
            .GreaterThan(x => x.StartTs!.Value)
            .When(x => x.StartTs.HasValue && x.EndTs.HasValue)
            .WithMessage("EndTs must be after StartTs");
        SharedRecurrenceRules.Apply(this, x => x.IsRecurring, x => x.RecurrenceRule);
    }
}

public class UpdateAvailabilityEventRequestValidator : RecurringWindowUpdateValidator<UpdateAvailabilityEventRequest> { }

public class AddScopeRequestValidator : AbstractValidator<AddScopeRequest>
{
    public AddScopeRequestValidator()
    {
        RuleFor(x => x.TargetType).IsInEnum();
        RuleFor(x => x.TargetId).NotEmpty();
        RuleFor(x => x.Effect).IsInEnum();
    }
}

public class CreateResourceAbsenceRequestValidator : AbstractValidator<CreateResourceAbsenceRequest>
{
    public CreateResourceAbsenceRequestValidator()
    {
        RuleFor(x => x.AbsenceType).IsInEnum();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.StartTs).NotEmpty();
        RuleFor(x => x.EndTs).NotEmpty().GreaterThan(x => x.StartTs).WithMessage("EndTs must be after StartTs");
        SharedRecurrenceRules.Apply(this, x => x.IsRecurring, x => x.RecurrenceRule);
    }
}

public class UpdateResourceAbsenceRequestValidator : RecurringWindowUpdateValidator<UpdateResourceAbsenceRequest> { }

internal static class SharedRecurrenceRules
{
    /// <summary>
    /// Adds the standard "recurrence_rule iff is_recurring" pair of rules.
    /// Works for both create (bool IsRecurring) and update (bool? IsRecurring)
    /// validators: the rules only fire when IsRecurring has a definite value.
    /// </summary>
    public static void Apply<T>(
        AbstractValidator<T> validator,
        Func<T, bool?> isRecurring,
        System.Linq.Expressions.Expression<Func<T, string?>> recurrenceRule)
    {
        validator.RuleFor(recurrenceRule)
            .NotEmpty().When(x => isRecurring(x) == true)
            .WithMessage("RecurrenceRule is required when IsRecurring is true");

        validator.RuleFor(recurrenceRule)
            .Null().When(x => isRecurring(x) == false)
            .WithMessage("RecurrenceRule must be null when IsRecurring is false");
    }
}

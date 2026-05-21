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
            .Must(x => !BeValidTime(x.WorkingDayStart) || !BeValidTime(x.WorkingDayEnd) ||
                        TimeSpan.Parse(x.WorkingDayStart) < TimeSpan.Parse(x.WorkingDayEnd))
            .WithName("WorkingDayEnd")
            .WithMessage("WorkingDayEnd must be after WorkingDayStart");

        RuleFor(x => x.PublicHolidayRegion)
            .MaximumLength(10)
            .When(x => x.PublicHolidayRegion != null);

        RuleFor(x => x.PublicHolidayRegion)
            .NotEmpty().When(x => x.PublicHolidaysEnabled)
            .WithMessage("PublicHolidayRegion is required when public holidays are enabled");
    }

    private static bool BeValidTime(string time) => TimeSpan.TryParse(time, out _);
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

public class UpdateAvailabilityEventRequestValidator : AbstractValidator<UpdateAvailabilityEventRequest>
{
    public UpdateAvailabilityEventRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title != null);
        RuleFor(x => x.EndTs)
            .GreaterThan(x => x.StartTs!.Value)
            .When(x => x.StartTs.HasValue && x.EndTs.HasValue)
            .WithMessage("EndTs must be after StartTs");
        RuleFor(x => x.RecurrenceRule)
            .NotEmpty().When(x => x.IsRecurring == true)
            .WithMessage("RecurrenceRule is required when IsRecurring is true");
        RuleFor(x => x.RecurrenceRule)
            .Null().When(x => x.IsRecurring == false)
            .WithMessage("RecurrenceRule must be null when IsRecurring is false");
    }
}

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

public class UpdateResourceAbsenceRequestValidator : AbstractValidator<UpdateResourceAbsenceRequest>
{
    public UpdateResourceAbsenceRequestValidator()
    {
        RuleFor(x => x.Title).MaximumLength(200).When(x => x.Title != null);
        RuleFor(x => x.EndTs)
            .GreaterThan(x => x.StartTs!.Value)
            .When(x => x.StartTs.HasValue && x.EndTs.HasValue)
            .WithMessage("EndTs must be after StartTs");
        RuleFor(x => x.RecurrenceRule)
            .NotEmpty().When(x => x.IsRecurring == true)
            .WithMessage("RecurrenceRule is required when IsRecurring is true");
        RuleFor(x => x.RecurrenceRule)
            .Null().When(x => x.IsRecurring == false)
            .WithMessage("RecurrenceRule must be null when IsRecurring is false");
    }
}

internal static class SharedRecurrenceRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, bool>> isRecurring,
        System.Linq.Expressions.Expression<Func<T, string?>> recurrenceRule)
    {
        validator.RuleFor(recurrenceRule)
            .NotEmpty().When(isRecurring.Compile())
            .WithMessage("RecurrenceRule is required when IsRecurring is true");

        validator.RuleFor(recurrenceRule)
            .Null().When(isRecurring.Compile().Negate())
            .WithMessage("RecurrenceRule must be null when IsRecurring is false");
    }
}

internal static class FuncExtensions
{
    public static Func<T, bool> Negate<T>(this Func<T, bool> fn) => x => !fn(x);
}

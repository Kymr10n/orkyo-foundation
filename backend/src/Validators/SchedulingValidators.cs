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

    private static bool BeValidTime(string time)
    {
        return TimeSpan.TryParse(time, out _);
    }
}

public class CreateOffTimeRequestValidator : AbstractValidator<CreateOffTimeRequest>
{
    public CreateOffTimeRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(200);

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Type must be Holiday, Maintenance, or Custom");

        RuleFor(x => x.StartTs)
            .NotEmpty().WithMessage("StartTs is required");

        RuleFor(x => x.EndTs)
            .NotEmpty().WithMessage("EndTs is required")
            .GreaterThan(x => x.StartTs).WithMessage("EndTs must be after StartTs");

        SharedOffTimeRules.Apply(this,
            x => x.IsRecurring, x => x.RecurrenceRule,
            x => !x.AppliesToAllSpaces, x => x.SpaceIds);

        RuleFor(x => x.RecurrenceRule)
            .Null().When(x => !x.IsRecurring)
            .WithMessage("RecurrenceRule must be null when IsRecurring is false");
    }
}

public class UpdateOffTimeRequestValidator : AbstractValidator<UpdateOffTimeRequest>
{
    public UpdateOffTimeRequestValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(200)
            .When(x => x.Title != null);

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Type must be Holiday, Maintenance, or Custom")
            .When(x => x.Type.HasValue);

        RuleFor(x => x.EndTs)
            .GreaterThan(x => x.StartTs!.Value)
            .When(x => x.StartTs.HasValue && x.EndTs.HasValue)
            .WithMessage("EndTs must be after StartTs");

        SharedOffTimeRules.Apply(this,
            x => x.IsRecurring == true, x => x.RecurrenceRule,
            x => x.AppliesToAllSpaces == false, x => x.SpaceIds);
    }
}

/// <summary>
/// Shared validation rules for create/update off-time requests.
/// </summary>
internal static class SharedOffTimeRules
{
    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, bool>> isRecurring,
        System.Linq.Expressions.Expression<Func<T, string?>> recurrenceRule,
        System.Linq.Expressions.Expression<Func<T, bool>> needsSpaces,
        System.Linq.Expressions.Expression<Func<T, List<Guid>?>> spaceIds)
    {
        validator.RuleFor(recurrenceRule)
            .NotEmpty().When(isRecurring.Compile())
            .WithMessage("RecurrenceRule is required when IsRecurring is true");

        validator.RuleFor(spaceIds)
            .NotEmpty().When(needsSpaces.Compile())
            .WithMessage("SpaceIds is required when AppliesToAllSpaces is false");
    }
}

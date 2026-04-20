using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class ScheduleRequestRequestValidator : AbstractValidator<ScheduleRequestRequest>
{
    public ScheduleRequestRequestValidator()
    {
        RuleFor(x => x).Custom((req, ctx) =>
        {
            var hasSpace = req.SpaceId.HasValue;
            var hasStart = req.StartTs.HasValue;
            var hasEnd = req.EndTs.HasValue;

            if (hasSpace && hasStart && hasEnd)
            {
                if (req.StartTs >= req.EndTs)
                    ctx.AddFailure("End time must be after start time");
                return;
            }

            if (!hasSpace && !hasStart && !hasEnd)
                return;

            ctx.AddFailure("To schedule, provide spaceId, startTs, and endTs. To unschedule, set all to null.");
        });
    }
}

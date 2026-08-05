using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateCalendarFeedRequestValidator : AbstractValidator<CreateCalendarFeedRequest>
{
    public CreateCalendarFeedRequestValidator()
    {
        // Matches calendar_feed_tokens.label — a longer value would be truncated
        // by the database rather than reported to the user.
        RuleFor(x => x.Label)
            .MaximumLength(100)
            .WithMessage("Label must be 100 characters or fewer");
    }
}

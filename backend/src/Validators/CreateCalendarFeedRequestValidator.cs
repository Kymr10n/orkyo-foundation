using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateCalendarFeedRequestValidator : AbstractValidator<CreateCalendarFeedRequest>
{
    public CreateCalendarFeedRequestValidator()
    {
        // Matches calendar_feed_tokens.label — Postgres rejects a longer value with an error,
        // which would reach the user as a 500 instead of this message.
        RuleFor(x => x.Label)
            .MaximumLength(100)
            .WithMessage("Label must be 100 characters or fewer");
    }
}

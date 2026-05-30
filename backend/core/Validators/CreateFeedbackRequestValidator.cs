using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateFeedbackRequestValidator : AbstractValidator<CreateFeedbackRequest>
{
    private static readonly HashSet<string> ValidTypes = ["bug", "feature", "question", "other"];

    public CreateFeedbackRequestValidator()
    {
        RuleFor(x => x.FeedbackType).NotEmpty()
            .Must(t => ValidTypes.Contains(t?.ToLower() ?? ""))
            .WithMessage("FeedbackType must be one of: bug, feature, question, other");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(DomainLimits.FeedbackTitleMaxLength);
    }
}

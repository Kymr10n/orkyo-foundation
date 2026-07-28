using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class UpdateFeedbackRequestValidator : AbstractValidator<UpdateFeedbackRequest>
{
    public const int AdminNotesMaxLength = 5000;

    public UpdateFeedbackRequestValidator()
    {
        // Cross-field: an all-null payload is a no-op the caller almost certainly did not intend.
        RuleFor(x => x)
            .Must(x => x.Status is not null || x.AdminNotes is not null || x.GithubIssueUrl is not null)
            .WithMessage("Provide at least one of: status, adminNotes, githubIssueUrl")
            .OverridePropertyName(string.Empty);

        // Null means "leave unchanged" on every field; only supplied values are checked.
        RuleFor(x => x.Status!)
            .Must(FeedbackStatuses.All.Contains)
            .WithMessage($"Status must be one of: {string.Join(", ", FeedbackStatuses.All)}")
            .When(x => x.Status is not null);
        RuleFor(x => x.AdminNotes!).MaximumLength(AdminNotesMaxLength)
            .When(x => x.AdminNotes is not null);
        RuleFor(x => x.GithubIssueUrl!)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var u)
                         && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
            .WithMessage("GithubIssueUrl must be an absolute http(s) URL")
            .When(x => !string.IsNullOrEmpty(x.GithubIssueUrl));
    }
}

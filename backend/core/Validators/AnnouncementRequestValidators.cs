using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for announcements. Channels are checked against
/// <see cref="AnnouncementChannels.All"/> — an unknown channel would otherwise be accepted and
/// then silently dropped at delivery.
/// </summary>
public class CreateAnnouncementRequestValidator : AbstractValidator<CreateAnnouncementRequest>
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 10_000;

    public CreateAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TitleMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(BodyMaxLength);
        RuleFor(x => x.RetentionDays!.Value).InclusiveBetween(1, 3650)
            .When(x => x.RetentionDays.HasValue);
        RuleForEach(x => x.Channels!)
            .Must(AnnouncementChannels.All.Contains)
            .WithMessage($"Channel must be one of: {string.Join(", ", AnnouncementChannels.All)}")
            .When(x => x.Channels is not null);
    }
}

public class UpdateAnnouncementRequestValidator : AbstractValidator<UpdateAnnouncementRequest>
{
    public UpdateAnnouncementRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CreateAnnouncementRequestValidator.TitleMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(CreateAnnouncementRequestValidator.BodyMaxLength);
    }
}

using Api.Models;
using Api.Models.Export;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for the auto-schedule horizon and the export payload. An inverted horizon
/// is the invariant that matters: the solver would otherwise be handed an empty window and
/// return "no solution" rather than reporting bad input.
/// </summary>
public class AutoSchedulePreviewRequestValidator : AbstractValidator<AutoSchedulePreviewRequest>
{
    public AutoSchedulePreviewRequestValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.HorizonEnd).GreaterThanOrEqualTo(x => x.HorizonStart)
            .WithMessage("HorizonEnd must be on or after HorizonStart");
        RuleForEach(x => x.RequestIds!).NotEmpty()
            .WithMessage("RequestIds must not contain empty GUIDs")
            .When(x => x.RequestIds is not null);
    }
}

public class AutoScheduleApplyRequestValidator : AbstractValidator<AutoScheduleApplyRequest>
{
    public AutoScheduleApplyRequestValidator()
    {
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.HorizonEnd).GreaterThanOrEqualTo(x => x.HorizonStart)
            .WithMessage("HorizonEnd must be on or after HorizonStart");
        RuleForEach(x => x.RequestIds!).NotEmpty()
            .WithMessage("RequestIds must not contain empty GUIDs")
            .When(x => x.RequestIds is not null);
    }
}

public class ExportRequestValidator : AbstractValidator<ExportRequest>
{
    public ExportRequestValidator() =>
        RuleForEach(x => x.SiteIds!).NotEmpty()
            .WithMessage("SiteIds must not contain empty GUIDs")
            .When(x => x.SiteIds is not null);
}

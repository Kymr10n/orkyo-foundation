using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class LinkResourceScanCodeRequestValidator : AbstractValidator<LinkResourceScanCodeRequest>
{
    public LinkResourceScanCodeRequestValidator()
    {
        // The service stores the trimmed text, so the limit applies to that.
        RuleFor(x => x.Code)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(code => code.Trim().Length <= DomainLimits.ResourceScanCodeMaxLength)
            .WithMessage($"Code must be {DomainLimits.ResourceScanCodeMaxLength} characters or fewer.");
    }
}

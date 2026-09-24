using Api.Constants;
using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class LinkResourceScanCodeRequestValidator : AbstractValidator<LinkResourceScanCodeRequest>
{
    public LinkResourceScanCodeRequestValidator()
    {
        // The service stores the trimmed text, so the limits apply to that.
        RuleFor(x => x.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code))
            .WithMessage("Code must not be empty.")
            .Must(code => code is null || code.Trim().Length <= DomainLimits.ResourceScanCodeMaxLength)
            .WithMessage($"Code must be {DomainLimits.ResourceScanCodeMaxLength} characters or fewer.");
    }
}

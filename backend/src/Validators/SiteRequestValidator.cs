using Api.Constants;
using Api.Endpoints;
using FluentValidation;

namespace Api.Validators;

public abstract class SiteRequestValidator<T> : AbstractValidator<T> where T : ISiteRequest
{
    protected SiteRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(DomainLimits.SiteCodeMaxLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(DomainLimits.SiteNameMaxLength);
    }
}

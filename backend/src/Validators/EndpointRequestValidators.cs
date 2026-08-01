using Api.Endpoints;
using Api.Endpoints.Reporting;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for the <c>*Request</c> records declared alongside their endpoints in the Web
/// assembly (Core cannot reference them). Registered by the same
/// <c>AddValidatorsFromAssemblyContaining&lt;RequestEmailChangeRequestValidator&gt;()</c> scan.
/// </summary>
public class AddGroupCapabilityRequestValidator : AbstractValidator<AddGroupCapabilityRequest>
{
    public AddGroupCapabilityRequestValidator() =>
        RuleFor(x => x.CriterionId).NotEmpty();
}

public class AddResourceCapabilityRequestValidator : AbstractValidator<AddResourceCapabilityRequest>
{
    public AddResourceCapabilityRequestValidator() =>
        RuleFor(x => x.CriterionId).NotEmpty();
}

public class CreateReportingTokenRequestValidator : AbstractValidator<CreateReportingTokenRequest>
{
    public const int NameMaxLength = 200;

    public CreateReportingTokenRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(NameMaxLength);
        // A token minted already-expired is silently useless; reject it at the boundary.
        RuleFor(x => x.ExpiresAt!.Value).GreaterThan(DateTime.UtcNow)
            .WithMessage("ExpiresAt must be in the future")
            .When(x => x.ExpiresAt.HasValue);
    }
}

/// <summary>
/// Structural envelope only. Value semantics (type, range, format per descriptor) are owned by
/// <see cref="Api.Services.TenantSettingsValidator"/> and deliberately not restated here — that
/// policy is descriptor-driven and cannot be expressed statically.
/// </summary>
public class UpdateSettingsRequestValidator : AbstractValidator<UpdateSettingsRequest>
{
    public UpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Settings).NotEmpty().WithMessage("Settings must contain at least one entry");
        RuleForEach(x => x.Settings.Keys).NotEmpty().WithMessage("Setting keys must not be blank")
            .When(x => x.Settings is not null);
    }
}

/// <summary>Admin-surface twin of <see cref="UpdateSettingsRequestValidator"/>; same envelope rules.</summary>
public class AdminUpdateSettingsRequestValidator : AbstractValidator<Api.Endpoints.Admin.UpdateSettingsRequest>
{
    public AdminUpdateSettingsRequestValidator()
    {
        RuleFor(x => x.Settings).NotEmpty().WithMessage("Settings must contain at least one entry");
        RuleForEach(x => x.Settings.Keys).NotEmpty().WithMessage("Setting keys must not be blank")
            .When(x => x.Settings is not null);
    }
}

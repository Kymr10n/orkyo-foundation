using Api.Models;
using FluentValidation;

namespace Api.Validators;

/// <summary>
/// Shape validators for request requirements. <c>Value</c> is a <c>JsonElement</c> whose
/// interpretation depends on the criterion's datatype, so it is validated downstream by the
/// criterion machinery — only the structural invariants belong here.
/// </summary>
public class CreateRequestRequirementRequestValidator : AbstractValidator<CreateRequestRequirementRequest>
{
    public CreateRequestRequirementRequestValidator()
    {
        RuleFor(x => x.CriterionId).NotEmpty();
        RuleFor(x => x.Operator!).MaximumLength(20).When(x => x.Operator is not null);
    }
}

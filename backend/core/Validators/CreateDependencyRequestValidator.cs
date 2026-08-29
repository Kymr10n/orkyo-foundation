using Api.Models;
using FluentValidation;

namespace Api.Validators;

public class CreateDependencyRequestValidator : AbstractValidator<CreateDependencyRequest>
{
    public CreateDependencyRequestValidator()
    {
        RuleFor(x => x.PredecessorRequestId).NotEmpty().WithMessage("A predecessor request is required");

        // Lag only ever delays a successor. A negative value would ask the scheduler to start
        // work before the thing it waits for has finished, which is the edge's whole point.
        RuleFor(x => x.LagMinutes).GreaterThanOrEqualTo(0).WithMessage("Lag cannot be negative");
    }
}

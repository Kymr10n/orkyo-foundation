using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class CreateDependencyRequestValidatorTests
{
    private readonly IValidator<CreateDependencyRequest> _validator = new CreateDependencyRequestValidator();

    [Fact]
    public void ValidEdge_Passes()
    {
        var result = _validator.Validate(new CreateDependencyRequest
        {
            PredecessorRequestId = Guid.NewGuid(),
            LagMinutes = 480
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ZeroLag_Passes()
    {
        var result = _validator.Validate(new CreateDependencyRequest
        {
            PredecessorRequestId = Guid.NewGuid()
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NegativeLag_Fails()
    {
        // A negative lag would ask the scheduler to start a successor before the thing it
        // waits for has finished.
        var result = _validator.Validate(new CreateDependencyRequest
        {
            PredecessorRequestId = Guid.NewGuid(),
            LagMinutes = -1
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDependencyRequest.LagMinutes));
    }

    [Fact]
    public void EmptyPredecessor_Fails()
    {
        var result = _validator.Validate(new CreateDependencyRequest
        {
            PredecessorRequestId = Guid.Empty
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(CreateDependencyRequest.PredecessorRequestId));
    }
}

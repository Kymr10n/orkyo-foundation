using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class RoutingRequestValidatorsTests
{
    private readonly IValidator<CreateRoutingRequest> _create = new CreateRoutingRequestValidator();
    private readonly IValidator<InstantiateRoutingRequest> _instantiate = new InstantiateRoutingRequestValidator();

    private static RoutingStepRequest Step(int no, int setup = 0, int run = 10, int lag = 0) => new()
    {
        StepNo = no,
        OperationTemplateId = Guid.NewGuid(),
        SetupMinutes = setup,
        RunMinutesPerUnit = run,
        LagMinutesAfter = lag,
    };

    [Fact]
    public void StepsNumberedInOrder_Pass()
    {
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(1), Step(2), Step(3)] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NoSteps_Fails()
    {
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [] });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one step"));
    }

    [Theory]
    [InlineData(1, 3)]   // a gap
    [InlineData(0, 1)]   // numbering starts at 1
    [InlineData(2, 2)]   // a duplicate leaves step 1 unfilled
    public void StepsWithGaps_Fail(int first, int second)
    {
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(first), Step(second)] });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("without gaps"));
    }

    [Fact]
    public void StepsOutOfOrder_Pass()
    {
        // The array order is how the client happened to send them; the step numbers are the work
        // order, and reads return them by number. Only gaps and duplicates are errors.
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(2), Step(1)] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void AStepThatTakesNoTime_Fails()
    {
        // setup 0 + run 0 would create a zero-length request whatever the quantity.
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(1, setup: 0, run: 0)] });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must take time"));
    }

    [Fact]
    public void SetupOnly_Passes()
    {
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(1, setup: 20, run: 0)] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NegativeMinutes_Fail()
    {
        var result = _create.Validate(new CreateRoutingRequest { Name = "Bracket", Steps = [Step(1, lag: -5)] });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Instantiate_NeedsAPositiveQuantity()
    {
        var result = _instantiate.Validate(new InstantiateRoutingRequest { Name = "WO-1", Quantity = 0 });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(InstantiateRoutingRequest.Quantity));
    }

    [Fact]
    public void Instantiate_WindowMustBeOrdered()
    {
        var result = _instantiate.Validate(new InstantiateRoutingRequest
        {
            Name = "WO-1",
            Quantity = 1,
            EarliestStartTs = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            LatestEndTs = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("after EarliestStartTs"));
    }

    [Fact]
    public void Instantiate_WithoutAWindow_Passes()
    {
        var result = _instantiate.Validate(new InstantiateRoutingRequest { Name = "WO-1", Quantity = 40 });

        Assert.True(result.IsValid);
    }

    // ── Update carries the same step rules ───────────────────────────

    private readonly IValidator<UpdateRoutingRequest> _update = new UpdateRoutingRequestValidator();

    [Fact]
    public void Update_StepsNumberedInOrder_Pass()
    {
        var result = _update.Validate(new UpdateRoutingRequest { Name = "Bracket", Steps = [Step(1), Step(2)] });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_NoSteps_Fails()
    {
        var result = _update.Validate(new UpdateRoutingRequest { Name = "Bracket", Steps = [] });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one step"));
    }

    [Fact]
    public void Update_GapInNumbering_Fails()
    {
        var result = _update.Validate(new UpdateRoutingRequest { Name = "Bracket", Steps = [Step(1), Step(3)] });

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("without gaps"));
    }

    [Fact]
    public void Update_EmptyName_Fails()
    {
        var result = _update.Validate(new UpdateRoutingRequest { Name = "", Steps = [Step(1)] });

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRoutingRequest.Name));
    }
}

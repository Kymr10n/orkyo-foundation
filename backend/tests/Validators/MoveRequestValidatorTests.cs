using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class MoveRequestValidatorTests
{
    private readonly IValidator<MoveRequestRequest> _validator = new MoveRequestRequestValidator();

    [Fact]
    public void ValidRequest_ZeroSortOrder_Passes()
    {
        var request = new MoveRequestRequest { SortOrder = 0 };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidRequest_PositiveSortOrder_Passes()
    {
        var request = new MoveRequestRequest { SortOrder = 5 };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void InvalidRequest_NegativeSortOrder_Fails()
    {
        var request = new MoveRequestRequest { SortOrder = -1 };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "SortOrder");
    }

    [Fact]
    public void ValidRequest_WithNewParent_Passes()
    {
        var request = new MoveRequestRequest
        {
            NewParentRequestId = Guid.NewGuid(),
            SortOrder = 3
        };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidRequest_NullNewParent_Passes()
    {
        var request = new MoveRequestRequest
        {
            NewParentRequestId = null,
            SortOrder = 0
        };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }
}

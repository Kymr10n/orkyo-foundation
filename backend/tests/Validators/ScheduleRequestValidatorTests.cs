using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class ScheduleRequestValidatorTests
{
    private readonly IValidator<ScheduleRequestRequest> _validator = new ScheduleRequestRequestValidator();

    [Fact]
    public void Schedule_AllFieldsProvided_Passes()
    {
        var now = DateTime.UtcNow;
        var request = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = now,
            EndTs = now.AddHours(2)
        };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unschedule_AllFieldsNull_Passes()
    {
        var request = new ScheduleRequestRequest
        {
            SpaceId = null,
            StartTs = null,
            EndTs = null
        };
        var result = _validator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Schedule_EndBeforeStart_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = now,
            EndTs = now.AddHours(-1)
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("End time must be after start time"));
    }

    [Fact]
    public void Schedule_SpaceIdOnly_Fails()
    {
        var request = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = null,
            EndTs = null
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("To schedule, provide spaceId, startTs, and endTs"));
    }

    [Fact]
    public void Schedule_MissingSpaceId_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new ScheduleRequestRequest
        {
            SpaceId = null,
            StartTs = now,
            EndTs = now.AddHours(2)
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schedule_MissingEndTs_Fails()
    {
        var request = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = DateTime.UtcNow,
            EndTs = null
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schedule_MissingStartTs_Fails()
    {
        var request = new ScheduleRequestRequest
        {
            SpaceId = Guid.NewGuid(),
            StartTs = null,
            EndTs = DateTime.UtcNow.AddHours(2)
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Schedule_OnlyStartAndEnd_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new ScheduleRequestRequest
        {
            SpaceId = null,
            StartTs = now,
            EndTs = now.AddHours(2)
        };
        var result = _validator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("To schedule"));
    }
}

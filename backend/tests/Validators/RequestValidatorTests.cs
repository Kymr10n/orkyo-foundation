using Api.Constants;
using Api.Models;
using Api.Validators;
using FluentValidation;
using Xunit;

namespace Api.Tests.Validators;

public class RequestValidatorTests
{
    private readonly IValidator<CreateRequestRequest> _createValidator = new CreateRequestRequestValidator();
    private readonly IValidator<UpdateRequestRequest> _updateValidator = new UpdateRequestRequestValidator();

    #region CreateRequestRequest

    [Fact]
    public void Create_ValidMinimalRequest_Passes()
    {
        var request = new CreateRequestRequest
        {
            Name = "Board Meeting",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_ValidFullRequest_Passes()
    {
        var now = DateTime.UtcNow;
        var request = new CreateRequestRequest
        {
            Name = "Board Meeting",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            StartTs = now,
            EndTs = now.AddHours(2),
            EarliestStartTs = now.AddDays(-1),
            LatestEndTs = now.AddDays(7),
            ActualDurationValue = 90,
            ActualDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Create_EmptyName_Fails(string? name)
    {
        var request = new CreateRequestRequest
        {
            Name = name!,
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = new string('x', DomainLimits.RequestNameMaxLength + 1),
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Theory]
    [InlineData("calendar")]
    [InlineData("hammer")]
    [InlineData(null)]
    public void Create_IconWithinLimit_Passes(string? icon)
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            Icon = icon,
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_IconTooLong_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            Icon = new string('x', DomainLimits.RequestIconMaxLength + 1),
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Icon");
    }

    [Fact]
    public void Create_IconAtExactLimit_Passes()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            Icon = new string('x', DomainLimits.RequestIconMaxLength),
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveDuration_Fails(int duration)
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = duration,
            MinimalDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("positive"));
    }

    [Fact]
    public void Create_StartWithoutEnd_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            StartTs = DateTime.UtcNow,
            EndTs = null
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Both start_ts and end_ts"));
    }

    [Fact]
    public void Create_EndWithoutStart_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            StartTs = null,
            EndTs = DateTime.UtcNow
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Both start_ts and end_ts"));
    }

    [Fact]
    public void Create_EndBeforeStart_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            StartTs = now,
            EndTs = now.AddHours(-1)
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("End time must be after start time"));
    }

    [Fact]
    public void Create_EarliestStartAfterLatestEnd_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            EarliestStartTs = now.AddDays(7),
            LatestEndTs = now
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Earliest start must be before latest end"));
    }

    [Fact]
    public void Create_ActualDurationValueWithoutUnit_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            ActualDurationValue = 90,
            ActualDurationUnit = null
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Both actual_duration_value and actual_duration_unit"));
    }

    [Fact]
    public void Create_ActualDurationUnitWithoutValue_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            ActualDurationValue = null,
            ActualDurationUnit = DurationUnit.Hours
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_NonPositiveActualDuration_Fails(int value)
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            ActualDurationValue = value,
            ActualDurationUnit = DurationUnit.Minutes
        };
        var result = _createValidator.Validate(request);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Actual duration value must be positive"));
    }

    [Fact]
    public void Create_NeitherStartNorEnd_Passes()
    {
        var request = new CreateRequestRequest
        {
            Name = "Test",
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes,
            StartTs = null,
            EndTs = null
        };
        var result = _createValidator.Validate(request);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Create_SummaryWithResources_Fails()
    {
        var request = new CreateRequestRequest
        {
            Name = "Summary Node",
            PlanningMode = PlanningMode.Summary,
            ResourceIds = [Guid.NewGuid()],
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Summary requests cannot have resources"));
    }

    [Fact]
    public void Create_ContainerWithStartEnd_Fails()
    {
        var now = DateTime.UtcNow;
        var request = new CreateRequestRequest
        {
            Name = "Container Node",
            PlanningMode = PlanningMode.Container,
            StartTs = now,
            EndTs = now.AddHours(1),
            MinimalDurationValue = 60,
            MinimalDurationUnit = DurationUnit.Minutes
        };

        var result = _createValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Container requests cannot have start_ts"));
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Container requests cannot have end_ts"));
    }

    #endregion

    #region UpdateRequestRequest

    [Fact]
    public void Update_EmptyRequest_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_ValidName_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest { Name = "Updated Name" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_EmptyName_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest { Name = "" });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Update_NameTooLong_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            Name = new string('x', DomainLimits.RequestNameMaxLength + 1)
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_ValidIcon_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest { Icon = "calendar" });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_NullIcon_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest { Icon = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_IconTooLong_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            Icon = new string('x', DomainLimits.RequestIconMaxLength + 1)
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Icon");
    }

    [Fact]
    public void Update_IconAtExactLimit_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            Icon = new string('x', DomainLimits.RequestIconMaxLength)
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Update_EarliestStartAfterLatestEnd_Fails()
    {
        var now = DateTime.UtcNow;
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            EarliestStartTs = now.AddDays(7),
            LatestEndTs = now
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Earliest start must be before latest end"));
    }

    [Fact]
    public void Update_ActualDurationMismatch_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            ActualDurationValue = 90,
            ActualDurationUnit = null
        });
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_NonPositiveActualDuration_Fails(int value)
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            ActualDurationValue = value,
            ActualDurationUnit = DurationUnit.Hours
        });
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Update_NonPositiveMinimalDuration_Fails(int value)
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            MinimalDurationValue = value
        });
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Minimal duration value must be positive"));
    }

    [Fact]
    public void Update_PositiveMinimalDuration_Passes()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            MinimalDurationValue = 30
        });
        Assert.True(result.IsValid);
    }

    #endregion

    #region Join conditions

    [Fact]
    public void Update_KWithoutALogic_Fails()
    {
        // The repository writes k only when the logic is present, so accepting this would return
        // 200 with the old value still stored — a write that looks accepted and did nothing.
        var result = _updateValidator.Validate(new UpdateRequestRequest { PredecessorLogicK = 2 });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UpdateRequestRequest.PredecessorLogic));
    }

    [Fact]
    public void Update_KOfNWithoutK_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            PredecessorLogic = PredecessorLogic.KOfN,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_KBelowOne_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            PredecessorLogic = PredecessorLogic.KOfN,
            PredecessorLogicK = 0,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_KAlongsideAnyLogic_Fails()
    {
        var result = _updateValidator.Validate(new UpdateRequestRequest
        {
            PredecessorLogic = PredecessorLogic.Any,
            PredecessorLogicK = 2,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Update_AWellFormedPair_Passes()
    {
        Assert.True(_updateValidator.Validate(new UpdateRequestRequest
        {
            PredecessorLogic = PredecessorLogic.KOfN,
            PredecessorLogicK = 2,
        }).IsValid);

        Assert.True(_updateValidator.Validate(new UpdateRequestRequest
        {
            PredecessorLogic = PredecessorLogic.Any,
        }).IsValid);
    }

    #endregion
}

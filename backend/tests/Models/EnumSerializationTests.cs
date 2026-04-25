using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

public class EnumSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    [Theory]
    [InlineData(PlanningMode.Leaf, "leaf")]
    [InlineData(PlanningMode.Summary, "summary")]
    [InlineData(PlanningMode.Container, "container")]
    public void PlanningMode_SerializesToLowercase(PlanningMode value, string expected)
    {
        var json = JsonSerializer.Serialize(value, Options);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData(DurationUnit.Minutes, "minutes")]
    [InlineData(DurationUnit.Hours, "hours")]
    [InlineData(DurationUnit.Days, "days")]
    [InlineData(DurationUnit.Weeks, "weeks")]
    [InlineData(DurationUnit.Months, "months")]
    [InlineData(DurationUnit.Years, "years")]
    public void DurationUnit_SerializesToLowercase(DurationUnit value, string expected)
    {
        var json = JsonSerializer.Serialize(value, Options);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData(RequestStatus.Planned, "planned")]
    [InlineData(RequestStatus.InProgress, "in_progress")]
    [InlineData(RequestStatus.Done, "done")]
    [InlineData(RequestStatus.Cancelled, "cancelled")]
    public void RequestStatus_SerializesToExpectedValue(RequestStatus value, string expected)
    {
        var json = JsonSerializer.Serialize(value, Options);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData(OffTimeType.Holiday, "holiday")]
    [InlineData(OffTimeType.Maintenance, "maintenance")]
    [InlineData(OffTimeType.Custom, "custom")]
    public void OffTimeType_SerializesToLowercase(OffTimeType value, string expected)
    {
        var json = JsonSerializer.Serialize(value, Options);
        json.Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData(PlanningMode.Leaf, "leaf")]
    [InlineData(PlanningMode.Summary, "summary")]
    [InlineData(PlanningMode.Container, "container")]
    public void PlanningMode_DeserializesFromLowercase(PlanningMode expected, string input)
    {
        var result = JsonSerializer.Deserialize<PlanningMode>($"\"{input}\"", Options);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(RequestStatus.InProgress, "in_progress")]
    [InlineData(RequestStatus.Done, "done")]
    public void RequestStatus_DeserializesFromExpectedValue(RequestStatus expected, string input)
    {
        var result = JsonSerializer.Deserialize<RequestStatus>($"\"{input}\"", Options);
        result.Should().Be(expected);
    }

    [Fact]
    public void RequestInfo_EnumFields_SerializeToExpectedCasing()
    {
        var request = new RequestInfo
        {
            Id = Guid.Empty,
            Name = "test",
            PlanningMode = PlanningMode.Summary,
            Status = RequestStatus.InProgress,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            Requirements = [],
            SchedulingSettingsApply = true,
        };

        var json = JsonSerializer.Serialize(request, Options);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("planningMode").GetString().Should().Be("summary");
        root.GetProperty("status").GetString().Should().Be("in_progress");
        root.GetProperty("minimalDurationUnit").GetString().Should().Be("days");
    }
}

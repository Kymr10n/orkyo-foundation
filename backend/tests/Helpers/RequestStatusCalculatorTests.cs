using Api.Helpers;
using Api.Models;

namespace Orkyo.Foundation.Tests.Helpers;

public class RequestStatusCalculatorTests
{
    private static readonly DateTime Now = new(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Deferred)]
    public void ManualStates_AreNeverDerived_EvenWhenRunningNow(RequestStatus manual)
    {
        // A request whose window contains "now" would derive to in_progress — but manual states win.
        var start = Now.AddHours(-1);
        var end = Now.AddHours(1);

        RequestStatusCalculator.Effective(manual, start, end, Now).Should().Be(manual);
    }

    [Theory]
    [InlineData(RequestStatus.New)]
    [InlineData(RequestStatus.InProgress)]
    [InlineData(RequestStatus.Done)]
    public void Unscheduled_IsAlwaysNew_RegardlessOfStored(RequestStatus stored)
    {
        RequestStatusCalculator.Effective(stored, null, null, Now).Should().Be(RequestStatus.New);
    }

    [Fact]
    public void ScheduledInFuture_IsNew()
    {
        var start = Now.AddHours(2);
        var end = Now.AddHours(4);

        RequestStatusCalculator.Effective(RequestStatus.New, start, end, Now).Should().Be(RequestStatus.New);
    }

    [Fact]
    public void ScheduledWindowContainsNow_IsInProgress_OverridingStored()
    {
        var start = Now.AddHours(-1);
        var end = Now.AddHours(1);

        // Stored value is irrelevant for the active lifecycle — derive from the window.
        RequestStatusCalculator.Effective(RequestStatus.New, start, end, Now).Should().Be(RequestStatus.InProgress);
        RequestStatusCalculator.Effective(RequestStatus.Done, start, end, Now).Should().Be(RequestStatus.InProgress);
    }

    [Fact]
    public void ScheduledInPast_IsDone()
    {
        var start = Now.AddHours(-4);
        var end = Now.AddHours(-2);

        RequestStatusCalculator.Effective(RequestStatus.New, start, end, Now).Should().Be(RequestStatus.Done);
    }

    [Fact]
    public void Boundaries_StartIsInclusive_EndIsExclusive()
    {
        // now == start → in progress (start is inclusive)
        RequestStatusCalculator.Effective(RequestStatus.New, Now, Now.AddHours(1), Now)
            .Should().Be(RequestStatus.InProgress);
        // now == end → done (end is exclusive)
        RequestStatusCalculator.Effective(RequestStatus.New, Now.AddHours(-1), Now, Now)
            .Should().Be(RequestStatus.Done);
    }
}

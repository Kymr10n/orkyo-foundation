using Api.Models;
using Api.Services.AutoSchedule;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class StartWindowsTests
{
    [Fact]
    public void NoOccupancy_IsTheWholeRange()
    {
        StartWindows.Subtract(10, 100, [], duration: 5)
            .Should().Equal(new StartWindow(10, 100));
    }

    [Fact]
    public void AnEmptyRange_HasNoWindows()
    {
        StartWindows.Subtract(50, 50, [], duration: 5).Should().BeEmpty();
        StartWindows.Subtract(60, 50, [], duration: 5).Should().BeEmpty();
    }

    [Fact]
    public void AnOccupancy_ForbidsEveryStartThatWouldReachIntoIt()
    {
        // [40, 60) with a duration of 10 forbids starts in [31, 60): 31 ends at 41, inside it;
        // 30 ends at 40, exactly touching, which is allowed.
        StartWindows.Subtract(0, 100, [(40, 60)], duration: 10)
            .Should().Equal(new StartWindow(0, 31), new StartWindow(60, 100));
    }

    [Fact]
    public void APoint_MayBeTouchedButNotSpanned()
    {
        // A zero-length occupancy at 50 with a duration of 10: starts 41..49 would span it.
        StartWindows.Subtract(0, 100, [(50, 50)], duration: 10)
            .Should().Equal(new StartWindow(0, 41), new StartWindow(50, 100));
    }

    [Fact]
    public void ADurationOfOne_ForbidsExactlyTheOccupiedMinutes()
    {
        StartWindows.Subtract(0, 10, [(3, 5)], duration: 1)
            .Should().Equal(new StartWindow(0, 3), new StartWindow(5, 10));
    }

    [Fact]
    public void OverlappingAndUnsortedOccupancies_MergeCleanly()
    {
        StartWindows.Subtract(0, 100, [(70, 80), (20, 40), (30, 50)], duration: 5)
            .Should().Equal(new StartWindow(0, 16), new StartWindow(50, 66), new StartWindow(80, 100));
    }

    [Fact]
    public void OccupancyCoveringTheRange_LeavesNothing()
    {
        StartWindows.Subtract(20, 40, [(0, 100)], duration: 5).Should().BeEmpty();
    }

    [Fact]
    public void EarliestFit_TakesTheFirstMinuteAtOrAfterTheBound()
    {
        var windows = new[] { new StartWindow(0, 50), new StartWindow(80, 100) };

        StartWindows.EarliestFit(windows, [], duration: 10, bound: 0).Should().Be(0);
        StartWindows.EarliestFit(windows, [], duration: 10, bound: 30).Should().Be(30);
        StartWindows.EarliestFit(windows, [], duration: 10, bound: 60).Should().Be(80);
        StartWindows.EarliestFit(windows, [], duration: 10, bound: 100).Should().BeNull();
    }

    [Fact]
    public void EarliestFit_StepsPastReservations()
    {
        var windows = new[] { new StartWindow(0, 100) };

        // [0,10) collides with the reservation at [5, 20); the next try is 20.
        StartWindows.EarliestFit(windows, [(5, 20)], duration: 10, bound: 0).Should().Be(20);
        // A reservation right at the end of the window leaves nothing.
        StartWindows.EarliestFit([new StartWindow(90, 100)], [(95, 200)], duration: 10, bound: 0).Should().BeNull();
    }
}

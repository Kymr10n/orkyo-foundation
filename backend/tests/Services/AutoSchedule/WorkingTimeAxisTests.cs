using Api.Models;
using Api.Services.AutoSchedule;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

/// <summary>
/// The axis is the one place calendar time and working minutes meet, so what matters is that
/// the two directions agree, that a boundary offset snaps the right way for a start and for an
/// end, and that DST leaves a working day the length the settings say.
/// </summary>
public class WorkingTimeAxisTests
{
    private static SchedulingSettingsInfo Settings(string timeZone = "UTC", bool weekends = false, bool hours = true)
        => SchedulingSettingsInfo.Default(Guid.NewGuid()) with
        {
            TimeZone = timeZone,
            WorkingHoursEnabled = hours,
            WorkingDayStart = new TimeOnly(8, 0),
            WorkingDayEnd = new TimeOnly(17, 0),
            WeekendsEnabled = weekends,
        };

    // Monday 13 April 2026 to Friday 17 April 2026.
    private static readonly DateOnly Monday = new(2026, 4, 13);
    private static readonly DateOnly Friday = new(2026, 4, 17);

    private static DateTime Utc(int day, int hour, int minute = 0)
        => new(2026, 4, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Identity_IsPlainMinutesSinceTheHorizonStart()
    {
        var axis = WorkingTimeAxis.Identity(Monday, Friday);

        axis.Length.Should().Be(5 * 1440);
        axis.ToOffset(Utc(14, 9, 30)).Should().Be(1440 + 9 * 60 + 30);
        axis.StartAt(1440 + 9 * 60 + 30).Should().Be(Utc(14, 9, 30));
        axis.EndAt(5 * 1440).Should().Be(Utc(18, 0));
    }

    [Fact]
    public void SettingsOff_OrARunThatIgnoresThem_IsTheIdentity()
    {
        var off = WorkingTimeAxis.Build(Monday, Friday, null, respectSchedulingSettings: true);
        var ignored = WorkingTimeAxis.Build(Monday, Friday, Settings(), respectSchedulingSettings: false);

        off.Length.Should().Be(5 * 1440);
        ignored.Length.Should().Be(5 * 1440);
    }

    [Fact]
    public void WorkingHours_CompressNightsAndWeekendsAway()
    {
        var axis = WorkingTimeAxis.Build(Monday, new DateOnly(2026, 4, 20), Settings(), true);

        // Five weekdays of nine hours; the weekend and the nights are not on the axis.
        axis.Length.Should().Be(6 * 9 * 60);
        axis.ToOffset(Utc(13, 8)).Should().Be(0);
        axis.ToOffset(Utc(13, 17)).Should().Be(540);
        axis.ToOffset(Utc(14, 8)).Should().Be(540, "the night between has no working minutes");
        axis.ToOffset(Utc(18, 12)).Should().Be(5 * 540, "Saturday noon is the Friday/Monday boundary");
        axis.ToOffset(Utc(20, 8, 1)).Should().Be(5 * 540 + 1);
    }

    [Fact]
    public void ABoundaryOffset_SnapsForwardForAStartAndBackwardForAnEnd()
    {
        var axis = WorkingTimeAxis.Build(Monday, Friday, Settings(), true);

        axis.StartAt(540).Should().Be(Utc(14, 8), "a placement starting at offset 540 begins Tuesday morning");
        axis.EndAt(540).Should().Be(Utc(13, 17), "a placement ending at offset 540 ends Monday evening");

        // Not at a boundary, both agree.
        axis.StartAt(600).Should().Be(Utc(14, 9));
        axis.EndAt(600).Should().Be(Utc(14, 9));

        // The very edges.
        axis.StartAt(0).Should().Be(Utc(13, 8));
        axis.EndAt(0).Should().Be(Utc(13, 8));
        axis.StartAt(axis.Length).Should().Be(Utc(17, 17));
        axis.EndAt(axis.Length).Should().Be(Utc(17, 17));
    }

    [Fact]
    public void ToOffset_AndStartAt_RoundTripEveryWorkingMinute()
    {
        var axis = WorkingTimeAxis.Build(Monday, Friday, Settings(), true);

        for (var offset = 0; offset <= axis.Length; offset++)
        {
            axis.ToOffset(axis.StartAt(offset)).Should().Be(offset);
            axis.ToOffsetEnd(axis.EndAt(offset)).Should().Be(offset);
        }
    }

    [Fact]
    public void ToOffset_FloorsAndToOffsetEnd_CeilsWithinAMinute()
    {
        var axis = WorkingTimeAxis.Identity(Monday, Friday);
        var instant = Utc(13, 0).AddSeconds(90);

        axis.ToOffset(instant).Should().Be(1);
        axis.ToOffsetEnd(instant).Should().Be(2);
    }

    [Fact]
    public void OutsideTheHorizon_ClampsToItsEdges()
    {
        var axis = WorkingTimeAxis.Build(Monday, Friday, Settings(), true);

        axis.ToOffset(Utc(1, 12)).Should().Be(0);
        axis.ToOffsetEnd(Utc(1, 12)).Should().Be(0);
        axis.ToOffset(Utc(30, 12)).Should().Be(axis.Length);
    }

    [Fact]
    public void WeekendsOnly_KeepsWholeWeekdays()
    {
        var axis = WorkingTimeAxis.Build(Monday, new DateOnly(2026, 4, 20), Settings(hours: false), true);

        axis.Length.Should().Be(6 * 1440);
        axis.ToOffset(Utc(19, 12)).Should().Be(5 * 1440, "Sunday noon sits on the Friday/Monday boundary");
        axis.StartAt(5 * 1440).Should().Be(Utc(20, 0));
        axis.EndAt(5 * 1440).Should().Be(Utc(18, 0));
    }

    [Theory]
    [InlineData(2026, 3, 27)] // week of the spring-forward transition (29 March)
    [InlineData(2026, 10, 23)] // week of the fall-back transition (25 October)
    public void DstTransitions_LeaveAWorkingDayNineHoursLong(int year, int month, int day)
    {
        var start = new DateOnly(year, month, day);
        var axis = WorkingTimeAxis.Build(start, start.AddDays(6), Settings("Europe/Berlin"), true);

        // Friday and the following Monday to Thursday: five nine-hour days either side of the change.
        axis.Length.Should().Be(5 * 540);

        for (var offset = 0; offset <= axis.Length; offset++)
            axis.ToOffset(axis.StartAt(offset)).Should().Be(offset);
    }

    [Fact]
    public void CorruptWorkingHours_AreRefusedRatherThanPlannedAgainst()
    {
        var corrupt = Settings() with { WorkingDayStart = new TimeOnly(17, 0), WorkingDayEnd = new TimeOnly(9, 0) };

        var act = () => WorkingTimeAxis.Build(Monday, Friday, corrupt, true);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Working hours are enabled*");
    }

    [Fact]
    public void AnOffsetOutsideTheAxis_IsAnError()
    {
        var axis = WorkingTimeAxis.Identity(Monday, Friday);

        var act = () => axis.StartAt(axis.Length + 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AHorizonWithNoWorkingTime_HasNoLengthAndRefusesEveryOffset()
    {
        // Saturday 18 and Sunday 19 April 2026 under a weekdays-only calendar.
        var axis = WorkingTimeAxis.Build(new DateOnly(2026, 4, 18), new DateOnly(2026, 4, 19), Settings(), true);

        axis.Length.Should().Be(0);
        var act = () => axis.StartAt(0);
        act.Should().Throw<InvalidOperationException>().WithMessage("*no working time*");
    }
}

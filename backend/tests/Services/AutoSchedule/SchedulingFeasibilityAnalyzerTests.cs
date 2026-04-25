using Api.Models;
using Api.Services.AutoSchedule;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class SchedulingFeasibilityAnalyzerTests
{
    private readonly SchedulingFeasibilityAnalyzer _analyzer = new();

    [Fact]
    public void ExactCapabilityFit_ProducesCandidates()
    {
        var criterion = Guid.NewGuid();
        var request = MakeRequest(criteria: new HashSet<Guid> { criterion });
        var space = MakeSpace(criteria: new HashSet<Guid> { criterion });

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().HaveCountGreaterThan(0);
        result.Candidates.Should().OnlyContain(c => c.RequestId == request.RequestId);
    }

    [Fact]
    public void MissingCapability_RejectsWithNoCompatibleSpace()
    {
        var required = Guid.NewGuid();
        var other = Guid.NewGuid();
        var request = MakeRequest(criteria: new HashSet<Guid> { required });
        var space = MakeSpace(criteria: new HashSet<Guid> { other });

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().ContainSingle(r =>
            r.RequestId == request.RequestId &&
            r.ReasonCode == SchedulingReasonCode.NoCompatibleSpace);
    }

    [Fact]
    public void MultipleRequiredCapabilities_SpaceMustSatisfyAll()
    {
        var c1 = Guid.NewGuid();
        var c2 = Guid.NewGuid();
        var request = MakeRequest(criteria: new HashSet<Guid> { c1, c2 });
        var partial = MakeSpace(criteria: new HashSet<Guid> { c1 });
        var full = MakeSpace(criteria: new HashSet<Guid> { c1, c2 });

        var result = _analyzer.Analyze(MakeProblem([request], [partial, full]));

        result.Candidates.Should().OnlyContain(c => c.SpaceId == full.SpaceId);
    }

    [Fact]
    public void NoRequiredCapabilities_MatchesAnySpace()
    {
        var request = MakeRequest(criteria: new HashSet<Guid>());
        var space = MakeSpace(criteria: new HashSet<Guid> { Guid.NewGuid() });

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().NotBeEmpty();
    }

    [Fact]
    public void StartOnlyConstraint_RespectsEarliestStart()
    {
        var request = MakeRequest(durationDays: 3, earliest: new DateOnly(2026, 5, 1));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().NotBeEmpty();
        result.Candidates.SelectMany(c => c.FeasibleStartDays)
            .Should().OnlyContain(d => d >= new DateOnly(2026, 5, 1));
    }

    [Fact]
    public void EndOnlyConstraint_RespectsLatestEnd()
    {
        var request = MakeRequest(durationDays: 3, latest: new DateOnly(2026, 4, 20));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().NotBeEmpty();
        result.Candidates.SelectMany(c => c.FeasibleStartDays)
            .Should().OnlyContain(d => d.AddDays(2) <= new DateOnly(2026, 4, 20));
    }

    [Fact]
    public void BothStartAndEndConstraint_NarrowsWindow()
    {
        var request = MakeRequest(
            durationDays: 3,
            earliest: new DateOnly(2026, 5, 1),
            latest: new DateOnly(2026, 5, 5));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().NotBeEmpty();
        var starts = result.Candidates.SelectMany(c => c.FeasibleStartDays).ToList();
        starts.Should().OnlyContain(d => d >= new DateOnly(2026, 5, 1));
        starts.Should().OnlyContain(d => d.AddDays(2) <= new DateOnly(2026, 5, 5));
    }

    [Fact]
    public void ImpossibleDateWindow_ProducesNoFeasibleStarts()
    {
        var request = MakeRequest(
            durationDays: 10,
            earliest: new DateOnly(2026, 5, 1),
            latest: new DateOnly(2026, 5, 3));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().Contain(r => r.RequestId == request.RequestId);
    }

    [Fact]
    public void ZeroDuration_RejectsWithInvalidDuration()
    {
        var request = MakeRequest(durationDays: 0);
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().ContainSingle(r =>
            r.RequestId == request.RequestId &&
            r.ReasonCode == SchedulingReasonCode.InvalidDuration);
    }

    [Fact]
    public void OverlappingFixedAssignment_BlocksFeasibleStarts()
    {
        var spaceId = Guid.NewGuid();
        var request = MakeRequest(
            durationDays: 3,
            earliest: new DateOnly(2026, 4, 14),
            latest: new DateOnly(2026, 4, 20));
        var space = MakeSpace(id: spaceId);

        var fixed1 = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 18));

        var result = _analyzer.Analyze(MakeProblem(
            [request], [space], fixedAssignments: [fixed1]));

        var starts = result.Candidates.SelectMany(c => c.FeasibleStartDays).ToList();
        foreach (var start in starts)
        {
            var end = start.AddDays(2);
            var conflicts = !(end < new DateOnly(2026, 4, 14) || start > new DateOnly(2026, 4, 18));
            conflicts.Should().BeFalse($"start {start} should not conflict with fixed occupancy");
        }
    }

    [Fact]
    public void AdjacentAssignments_AreAllowed()
    {
        var spaceId = Guid.NewGuid();
        var fixed1 = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 4, 16));
        var request = MakeRequest(
            durationDays: 2,
            earliest: new DateOnly(2026, 4, 17),
            latest: new DateOnly(2026, 4, 20));
        var space = MakeSpace(id: spaceId);

        var result = _analyzer.Analyze(MakeProblem(
            [request], [space], fixedAssignments: [fixed1]));

        result.Candidates.Should().NotBeEmpty();
        result.Candidates.SelectMany(c => c.FeasibleStartDays)
            .Should().Contain(new DateOnly(2026, 4, 17));
    }

    [Fact]
    public void CandidateRemovedWhenDurationCannotFitAnySlot()
    {
        var spaceId = Guid.NewGuid();
        var fixed1 = new FixedOccupancy(
            Guid.NewGuid(), spaceId,
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));
        var request = MakeRequest(durationDays: 5);
        var space = MakeSpace(id: spaceId);

        var result = _analyzer.Analyze(MakeProblem(
            [request], [space], fixedAssignments: [fixed1]));

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void CandidateRemovedWhenNoCompatibleSpaceExists()
    {
        var request = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });
        var result = _analyzer.Analyze(MakeProblem([request], []));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().Contain(r =>
            r.ReasonCode == SchedulingReasonCode.NoCompatibleSpace);
    }

    [Fact]
    public void DiagnosticsReportNoCompatibleSpaceCount()
    {
        var r1 = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });
        var r2 = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });

        var result = _analyzer.Analyze(MakeProblem([r1, r2], []));

        result.Diagnostics.Should().Contain(d => d.Contains("2 request(s) removed"));
    }

    [Fact]
    public void WeekendsExcluded_WhenSettingsDisableWeekends()
    {
        var settings = new SchedulingSettingsInfo
        {
            Id = Guid.NewGuid(),
            SiteId = Guid.NewGuid(),
            WeekendsEnabled = false,
            WorkingHoursEnabled = false,
            WorkingDayStart = new TimeOnly(8, 0),
            WorkingDayEnd = new TimeOnly(17, 0),
            PublicHolidaysEnabled = false,
            TimeZone = "UTC"
        };
        var request = MakeRequest(durationDays: 1, respectSettings: true);
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem(
            [request], [space], settings: settings));

        var starts = result.Candidates.SelectMany(c => c.FeasibleStartDays).ToList();
        starts.Should().NotContain(d =>
            d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday);
    }
}

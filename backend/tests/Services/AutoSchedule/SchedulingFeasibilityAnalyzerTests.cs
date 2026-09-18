using Api.Models;
using Api.Services.AutoSchedule;
using static Orkyo.Foundation.Tests.Services.AutoSchedule.AutoScheduleTestHelpers;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class SchedulingFeasibilityAnalyzerTests
{
    private readonly SchedulingFeasibilityAnalyzer _analyzer = new();

    private static IEnumerable<int> Starts(AnalyzedSchedulingProblem result)
        => result.Candidates.SelectMany(c => c.FeasibleStartWindows).SelectMany(w => Enumerable.Range(w.From, w.Length));

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
            r.ReasonCode == SchedulingReasonCode.NoCompatibleResource);
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

        result.Candidates.Should().OnlyContain(c => c.ResourceId == full.ResourceId);
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
        var request = MakeRequest(durationMinutes: Day(3), earliest: Day(17));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().ContainSingle()
            .Which.FeasibleStartWindows.Should().ContainSingle()
            .Which.From.Should().Be(Day(17));
    }

    [Fact]
    public void EndOnlyConstraint_RespectsLatestEnd()
    {
        var request = MakeRequest(durationMinutes: Day(3), latest: Day(7));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        // The last start still ends by the deadline: To is exclusive, so Day(4) + 1.
        result.Candidates.Should().ContainSingle()
            .Which.FeasibleStartWindows.Should().ContainSingle()
            .Which.To.Should().Be(Day(4) + 1);
    }

    [Fact]
    public void NoConstraint_TheWholeHorizonIsOpen()
    {
        var request = MakeRequest(durationMinutes: 60);
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        var window = result.Candidates.Single().FeasibleStartWindows.Single();
        window.From.Should().Be(0);
        window.To.Should().Be(Day(92) - 60 + 1, "14 Apr to 14 Jul inclusive is 92 days");
    }

    [Fact]
    public void BothStartAndEndConstraint_NarrowsWindow()
    {
        var request = MakeRequest(durationMinutes: Day(3), earliest: Day(17), latest: Day(21));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Single().FeasibleStartWindows.Should()
            .ContainSingle().Which.Should().Be(new StartWindow(Day(17), Day(18) + 1));
    }

    [Fact]
    public void ImpossibleWindow_ProducesNoFeasibleStarts()
    {
        var request = MakeRequest(durationMinutes: Day(10), earliest: Day(17), latest: Day(19));
        var space = MakeSpace();

        var result = _analyzer.Analyze(MakeProblem([request], [space]));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().Contain(r => r.RequestId == request.RequestId);
    }

    [Fact]
    public void ZeroDuration_RejectsWithInvalidDuration()
    {
        var request = MakeRequest(durationMinutes: 0);
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
        // Busy for the first five days, due by the end of the eighth: the only start that fits
        // a three-day job is the minute the occupancy ends.
        var resourceId = Guid.NewGuid();
        var request = MakeRequest(durationMinutes: Day(3), earliest: Day(0), latest: Day(8));
        var space = MakeSpace(id: resourceId);

        var fixed1 = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(5));

        var result = _analyzer.Analyze(MakeProblem([request], [space], fixedAssignments: [fixed1]));

        result.Candidates.Single().FeasibleStartWindows.Should()
            .ContainSingle().Which.Should().Be(new StartWindow(Day(5), Day(5) + 1));
        foreach (var start in Starts(result))
        {
            var end = start + Day(3);
            var conflicts = start < fixed1.End && end > fixed1.Start;
            conflicts.Should().BeFalse($"start {start} should not conflict with fixed occupancy");
        }
    }

    [Fact]
    public void AdjacentAssignments_AreAllowed()
    {
        // The minute the occupancy ends is a valid start; the minute before it is not.
        var resourceId = Guid.NewGuid();
        var fixed1 = new FixedOccupancy(Guid.NewGuid(), resourceId, 0, 90);
        var request = MakeRequest(durationMinutes: 30, latest: Day(1));
        var space = MakeSpace(id: resourceId);

        var result = _analyzer.Analyze(MakeProblem([request], [space], fixedAssignments: [fixed1]));

        result.Candidates.Single().FeasibleStartWindows.Should()
            .ContainSingle().Which.From.Should().Be(90);
    }

    [Fact]
    public void AnOccupancyInTheMiddle_SplitsTheWindowInTwo()
    {
        var resourceId = Guid.NewGuid();
        var busy = new FixedOccupancy(Guid.NewGuid(), resourceId, 100, 200);
        var request = MakeRequest(durationMinutes: 30, latest: 300);
        var space = MakeSpace(id: resourceId);

        var result = _analyzer.Analyze(MakeProblem([request], [space], fixedAssignments: [busy]));

        result.Candidates.Single().FeasibleStartWindows.Should().Equal(
            new StartWindow(0, 71),
            new StartWindow(200, 271));
    }

    [Fact]
    public void CandidateRemovedWhenDurationCannotFitAnySlot()
    {
        var resourceId = Guid.NewGuid();
        var fixed1 = new FixedOccupancy(Guid.NewGuid(), resourceId, Day(0), Day(92));
        var request = MakeRequest(durationMinutes: Day(5));
        var space = MakeSpace(id: resourceId);

        var result = _analyzer.Analyze(MakeProblem([request], [space], fixedAssignments: [fixed1]));

        result.Candidates.Should().BeEmpty();
    }

    [Fact]
    public void CandidateRemovedWhenNoCompatibleSpaceExists()
    {
        var request = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });
        var result = _analyzer.Analyze(MakeProblem([request], []));

        result.Candidates.Should().BeEmpty();
        result.Rejections.Should().Contain(r =>
            r.ReasonCode == SchedulingReasonCode.NoCompatibleResource);
    }

    [Fact]
    public void DiagnosticsReportNoCompatibleSpaceCount()
    {
        var r1 = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });
        var r2 = MakeRequest(criteria: new HashSet<Guid> { Guid.NewGuid() });

        var result = _analyzer.Analyze(MakeProblem([r1, r2], []));

        result.Diagnostics.Should().Contain(d => d.Contains("2 request(s) removed"));
    }
}

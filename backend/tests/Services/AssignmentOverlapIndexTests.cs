using Api.Constants;
using Api.Models;
using Api.Services;
using AwesomeAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The overlap index against the linear scan it replaced.
///
/// This is an optimization, so the only property that matters is that it returns exactly what the
/// obvious implementation returned — <c>a.StartUtc &lt; end &amp;&amp; a.EndUtc &gt; start</c>, minus the excluded
/// id. The randomised test below is the real guard: an interval index is easy to get subtly wrong
/// at the edges, and a wrong answer here would silently invent or hide booking conflicts.
/// </summary>
public class AssignmentOverlapIndexTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ResourceAssignmentInfo At(int startHour, int hours, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        RequestId = Guid.NewGuid(),
        ResourceId = Guid.NewGuid(),
        ResourceTypeKey = "room",
        StartUtc = Base.AddHours(startHour),
        EndUtc = Base.AddHours(startHour + hours),
        AssignmentStatus = AssignmentStatuses.Confirmed,
    };

    /// <summary>The implementation this replaced, kept as the oracle.</summary>
    private static List<ResourceAssignmentInfo> Naive(
        IEnumerable<ResourceAssignmentInfo> all, DateTime start, DateTime end, Guid? excludeId) =>
        all.Where(a => a.StartUtc < end && a.EndUtc > start)
           .Where(a => excludeId is null || a.Id != excludeId)
           .ToList();

    [Fact]
    public void FindsTheOverlapsAndNothingElse()
    {
        var overlapping = At(9, 2);      // 09:00–11:00, inside the window
        var before = At(0, 2);           // 00:00–02:00
        var after = At(20, 2);           // 20:00–22:00
        var index = new AssignmentOverlapIndex([before, overlapping, after]);

        var found = index.Overlapping(Base.AddHours(8), Base.AddHours(12), excludeId: null);

        found.Should().ContainSingle().Which.Id.Should().Be(overlapping.Id);
    }

    [Fact]
    public void TouchingAtAnEndpointIsNotAnOverlap()
    {
        // Half-open on both sides: a job ending exactly when the next begins does not clash, which
        // is what makes back-to-back scheduling possible at all.
        var index = new AssignmentOverlapIndex([At(6, 2), At(10, 2)]); // …–08:00 and 10:00–…

        index.Overlapping(Base.AddHours(8), Base.AddHours(10), excludeId: null).Should().BeEmpty();
    }

    [Fact]
    public void FindsALongAssignmentThatStartedWellBeforeTheWindow()
    {
        // The case a sort-by-start scan gets wrong. Everything between this and the window starts
        // and ends outside it, so a scan that stops at the first non-overlapping neighbour misses
        // the one booking that actually clashes.
        var longRunning = At(0, 100);
        var fillers = Enumerable.Range(1, 20).Select(i => At(i, 0)).ToList(); // zero-length, never overlap
        var index = new AssignmentOverlapIndex(fillers.Append(longRunning));

        var found = index.Overlapping(Base.AddHours(50), Base.AddHours(60), excludeId: null);

        found.Should().ContainSingle().Which.Id.Should().Be(longRunning.Id);
    }

    [Fact]
    public void ExcludesTheAssignmentBeingRevalidated()
    {
        // Validating an existing booking must not find itself and report a clash with itself.
        var self = At(9, 2);
        var index = new AssignmentOverlapIndex([self, At(9, 2)]);

        index.Overlapping(Base.AddHours(9), Base.AddHours(11), self.Id).Should().ContainSingle();
    }

    [Fact]
    public void EmptyIndexFindsNothing()
    {
        new AssignmentOverlapIndex([]).Overlapping(Base, Base.AddHours(1), null).Should().BeEmpty();
    }

    [Fact]
    public void AgreesWithTheLinearScanOnRandomisedData()
    {
        // Fixed seed: a failure has to be reproducible, and this is exactly the kind of code where
        // an off-by-one shows up only on one arrangement in a thousand.
        var random = new Random(1337);
        for (var trial = 0; trial < 200; trial++)
        {
            var assignments = Enumerable.Range(0, random.Next(0, 40))
                .Select(_ => At(random.Next(0, 48), random.Next(0, 12)))
                .ToList();
            var index = new AssignmentOverlapIndex(assignments);

            var excludeId = assignments.Count > 0 && random.Next(2) == 0
                ? assignments[random.Next(assignments.Count)].Id
                : (Guid?)null;
            var start = Base.AddHours(random.Next(0, 48));
            var end = start.AddHours(random.Next(0, 12));

            index.Overlapping(start, end, excludeId)
                .Select(a => a.Id).OrderBy(id => id)
                .Should().Equal(
                    Naive(assignments, start, end, excludeId).Select(a => a.Id).OrderBy(id => id),
                    $"trial {trial} window {start:o}–{end:o}");
        }
    }
}

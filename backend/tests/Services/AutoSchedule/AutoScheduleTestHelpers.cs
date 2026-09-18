using Api.Models;
using Api.Services.AutoSchedule;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

/// <summary>
/// Fixtures on the 24x7 identity axis, where an offset is plain minutes since the horizon
/// start. <see cref="Day"/> keeps the whole-day scenarios readable: <c>Day(3)</c> is the offset
/// of the fourth horizon day's midnight, and a "three-day" duration is <c>Day(3)</c> minutes.
/// </summary>
internal static class AutoScheduleTestHelpers
{
    internal const int MinutesPerDay = 24 * 60;

    /// <summary>The one type every fixture uses unless it says otherwise.</summary>
    internal const string Space = "space";

    /// <summary>The resource a single-type placement landed on.</summary>
    internal static Guid ResourceId(this ScheduledPlacement placement)
        => placement.Resources.Single().ResourceId;

    internal static readonly DateOnly DefaultHorizonStart = new(2026, 4, 14);
    internal static readonly DateOnly DefaultHorizonEnd = new(2026, 7, 14);

    internal static int Day(int n) => n * MinutesPerDay;

    internal static WorkingTimeAxis Identity(DateOnly? horizonStart = null, DateOnly? horizonEnd = null)
        => WorkingTimeAxis.Identity(horizonStart ?? DefaultHorizonStart, horizonEnd ?? DefaultHorizonEnd);

    internal static SchedulingProblem MakeProblem(
        IReadOnlyList<RequestNode> requests,
        IReadOnlyList<ResourceNode> spaces,
        IReadOnlyList<FixedOccupancy>? fixedAssignments = null,
        DateOnly? horizonStart = null,
        DateOnly? horizonEnd = null,
        WorkingTimeAxis? axis = null,
        IReadOnlyList<DependencyEdge>? dependencies = null,
        IReadOnlyDictionary<Guid, IReadOnlySet<string>>? criterionTypeScopes = null)
        => new(
            SiteId: Guid.NewGuid(),
            HorizonStart: horizonStart ?? DefaultHorizonStart,
            HorizonEnd: horizonEnd ?? DefaultHorizonEnd,
            Axis: axis ?? Identity(horizonStart, horizonEnd),
            Requests: requests,
            Resources: spaces,
            FixedAssignments: fixedAssignments ?? [],
            Dependencies: dependencies,
            CriterionTypeScopes: criterionTypeScopes);

    internal static AnalyzedSchedulingProblem MakeAnalyzed(
        IReadOnlyList<SchedulingCandidate> candidates,
        IReadOnlyList<CandidateRejection>? rejections = null,
        IReadOnlyList<FixedOccupancy>? fixedAssignments = null,
        DateOnly? horizonStart = null,
        DateOnly? horizonEnd = null,
        IReadOnlyList<DependencyEdge>? dependencies = null)
        => new(
            Problem: MakeProblem([], [], fixedAssignments, horizonStart, horizonEnd, dependencies: dependencies),
            Candidates: candidates,
            Rejections: rejections ?? [],
            Diagnostics: []);

    /// <param name="windows">Where the start may fall; by default anywhere in the first 30 days.</param>
    internal static SchedulingCandidate MakeCandidate(
        Guid? requestId = null,
        Guid? resourceId = null,
        int durationMinutes = 3 * MinutesPerDay,
        int priority = 1,
        IReadOnlyList<StartWindow>? windows = null,
        string typeKey = Space)
    {
        var starts = windows ?? [new StartWindow(0, Day(30))];
        return new(
            RequestId: requestId ?? Guid.NewGuid(),
            ResourceId: resourceId ?? Guid.NewGuid(),
            ResourceTypeKey: typeKey,
            EarliestStart: starts.Min(w => w.From),
            LatestEnd: starts.Max(w => w.To) - 1 + durationMinutes,
            DurationMinutes: durationMinutes,
            Priority: priority,
            FeasibleStartWindows: starts);
    }

    internal static RequestNode MakeRequest(
        Guid? id = null,
        string name = "Test Request",
        int durationMinutes = 5 * MinutesPerDay,
        int priority = 1,
        int? earliest = null,
        int? latest = null,
        IReadOnlySet<Guid>? criteria = null,
        IReadOnlySet<string>? openTypes = null)
        => new(
            RequestId: id ?? Guid.NewGuid(),
            DisplayName: name,
            EarliestStart: earliest,
            LatestEnd: latest,
            DurationMinutes: durationMinutes,
            Priority: priority,
            RequiredCriterionIds: criteria ?? new HashSet<Guid>(),
            OpenTypeKeys: openTypes ?? new HashSet<string> { Space });

    internal static ResourceNode MakeSpace(
        Guid? id = null,
        string name = "Test Space",
        IReadOnlySet<Guid>? criteria = null,
        string typeKey = Space)
        => new(
            ResourceId: id ?? Guid.NewGuid(),
            DisplayName: name,
            ResourceTypeKey: typeKey,
            CriterionIds: criteria ?? new HashSet<Guid>());
}

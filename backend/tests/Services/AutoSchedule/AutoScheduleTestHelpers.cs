using Api.Models;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

internal static class AutoScheduleTestHelpers
{
    internal static readonly DateOnly DefaultHorizonStart = new(2026, 4, 14);
    internal static readonly DateOnly DefaultHorizonEnd = new(2026, 7, 14);

    internal static SchedulingProblem MakeProblem(
        IReadOnlyList<RequestNode> requests,
        IReadOnlyList<SpaceNode> spaces,
        IReadOnlyList<FixedOccupancy>? fixedAssignments = null,
        DateOnly? horizonStart = null,
        DateOnly? horizonEnd = null,
        SchedulingSettingsInfo? settings = null,
        List<OffTimeInfo>? offTimes = null)
        => new(
            SiteId: Guid.NewGuid(),
            HorizonStart: horizonStart ?? DefaultHorizonStart,
            HorizonEnd: horizonEnd ?? DefaultHorizonEnd,
            Requests: requests,
            Spaces: spaces,
            FixedAssignments: fixedAssignments ?? [],
            Settings: settings,
            OffTimes: offTimes,
            Mode: AutoScheduleMode.FillGapsOnly);

    internal static AnalyzedSchedulingProblem MakeAnalyzed(
        IReadOnlyList<SchedulingCandidate> candidates,
        IReadOnlyList<CandidateRejection>? rejections = null,
        IReadOnlyList<FixedOccupancy>? fixedAssignments = null,
        DateOnly? horizonStart = null,
        DateOnly? horizonEnd = null)
        => new(
            Problem: new SchedulingProblem(
                SiteId: Guid.NewGuid(),
                HorizonStart: horizonStart ?? DefaultHorizonStart,
                HorizonEnd: horizonEnd ?? DefaultHorizonEnd,
                Requests: [],
                Spaces: [],
                FixedAssignments: fixedAssignments ?? [],
                Settings: null,
                OffTimes: null,
                Mode: AutoScheduleMode.FillGapsOnly),
            Candidates: candidates,
            Rejections: rejections ?? [],
            Diagnostics: []);

    internal static SchedulingCandidate MakeCandidate(
        Guid? requestId = null,
        Guid? spaceId = null,
        int durationDays = 3,
        int priority = 1,
        IReadOnlyList<DateOnly>? feasibleStarts = null)
    {
        var starts = feasibleStarts ?? Enumerable.Range(0, 30)
            .Select(i => DefaultHorizonStart.AddDays(i))
            .ToList();
        return new(
            RequestId: requestId ?? Guid.NewGuid(),
            SpaceId: spaceId ?? Guid.NewGuid(),
            EarliestStart: starts.First(),
            LatestEnd: starts.Last().AddDays(durationDays - 1),
            DurationDays: durationDays,
            Priority: priority,
            FeasibleStartDays: starts);
    }

    internal static RequestNode MakeRequest(
        Guid? id = null,
        string name = "Test Request",
        int durationDays = 5,
        int priority = 1,
        DateOnly? earliest = null,
        DateOnly? latest = null,
        IReadOnlySet<Guid>? criteria = null,
        bool respectSettings = false)
        => new(
            RequestId: id ?? Guid.NewGuid(),
            DisplayName: name,
            EarliestStart: earliest,
            LatestEnd: latest,
            DurationDays: durationDays,
            Priority: priority,
            RespectSchedulingSettings: respectSettings,
            RequiredCriterionIds: criteria ?? new HashSet<Guid>());

    internal static SpaceNode MakeSpace(
        Guid? id = null,
        string name = "Test Space",
        IReadOnlySet<Guid>? criteria = null)
        => new(
            SpaceId: id ?? Guid.NewGuid(),
            DisplayName: name,
            CriterionIds: criteria ?? new HashSet<Guid>());
}

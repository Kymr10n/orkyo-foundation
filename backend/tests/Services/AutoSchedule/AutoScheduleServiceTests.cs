using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Security.Features;
using Api.Services;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class AutoScheduleServiceTests
{
    private static TenantSettings MakeSettings(bool autoScheduleEnabled = true)
        => new() { AutoSchedule_Enabled = autoScheduleEnabled };

    private static ResourceTypeInfo Type(string key, bool active = true, bool directory = false) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        DisplayName = key,
        DisplayNamePlural = key,
        HasGeometry = !directory,
        HasDirectoryProfile = directory,
        SingleGroupMembership = false,
        IsSystem = false,
        IsActive = active,
    };

    private static AutoScheduleService CreateService(
        IFeatureGate? featureGate = null,
        TenantSettings? settings = null,
        IEnumerable<ISchedulingSolver>? solvers = null,
        IReadOnlyList<ResourceTypeInfo>? types = null,
        IReadOnlyList<WithheldRequestNode>? withheld = null,
        IRequestRepository? requestRepository = null,
        WorkingTimeAxis? axis = null)
    {
        var mockProblemBuilder = new Mock<SchedulingProblemBuilder>(
            Mock.Of<IRequestRepository>(),
            Mock.Of<IResourceRepository>(),
            Mock.Of<IResourceCapabilityRepository>(),
            Mock.Of<ISchedulingRepository>(),
            Mock.Of<IAvailabilityResolver>(),
            Mock.Of<IRequestDependencyRepository>(),
            Mock.Of<ICriteriaRepository>());

        var problem = new SchedulingProblem(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 14),
            new DateOnly(2026, 7, 14),
            axis ?? WorkingTimeAxis.Identity(new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14)),
            [], [], [], null, withheld);

        mockProblemBuilder
            .Setup(x => x.BuildAsync(It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(problem);

        var analyzer = new SchedulingFeasibilityAnalyzer();
        var resolvedSolvers = solvers ?? [new GreedySchedulingSolver()];

        var mockSettingsService = new Mock<ITenantSettingsService>();
        mockSettingsService
            .Setup(x => x.GetSettingsAsync())
            .ReturnsAsync(settings ?? MakeSettings());

        // Default: all features enabled (mirrors Community / foundation standalone behaviour)
        var gate = featureGate ?? new AllFeaturesEnabledGate();

        // One schedulable type by default, so an omitted resourceTypeKeys resolves cleanly.
        var typeRepo = new Mock<IResourceTypeRepository>();
        typeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((types ?? [Type("space")]).ToList());

        return new AutoScheduleService(
            mockProblemBuilder.Object,
            analyzer,
            resolvedSolvers,
            requestRepository ?? Mock.Of<IRequestRepository>(),
            typeRepo.Object,
            gate,
            mockSettingsService.Object,
            NullLogger<AutoScheduleService>.Instance);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsFingerprintInResponse()
    {
        var service = CreateService();
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));

        var result = await service.PreviewAsync(request, CancellationToken.None);

        result.Fingerprint.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PreviewAsync_ReportsWithheldRequestsAsUnscheduled()
    {
        // The builder keeps dependency-blocked requests out of the solve set, so no solver can
        // report them. Without this the run answers with fewer requests than it was given and
        // says nothing about the difference.
        var blockedId = Guid.NewGuid();
        var service = CreateService(withheld: [new WithheldRequestNode(blockedId, "Grind")]);
        var request = new AutoSchedulePreviewRequest(
            Guid.NewGuid(), new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14),
            ResourceTypeKeys: [ResourceTypeKeys.Space]);

        var result = await service.PreviewAsync(request, CancellationToken.None);

        var entry = result.Unscheduled.Should().ContainSingle(u => u.RequestId == blockedId).Subject;
        entry.RequestName.Should().Be("Grind");
        entry.ReasonCodes.Should().Contain(SchedulingReasonCode.PredecessorUnscheduled);
    }

    [Fact]
    public async Task PreviewAsync_ThrowsWhenFeatureGateBlocks()
    {
        var blockedGate = new Mock<IFeatureGate>();
        blockedGate
            .Setup(g => g.EnsureEnabledAsync(FeatureKeys.AutoSchedule, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureNotAvailableException(FeatureKeys.AutoSchedule, "not on this plan"));

        var service = CreateService(featureGate: blockedGate.Object);
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));

        var act = () => service.PreviewAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<FeatureNotAvailableException>();
    }

    [Fact]
    public async Task PreviewAsync_ThrowsWhenAutoScheduleDisabled()
    {
        var service = CreateService(settings: MakeSettings(autoScheduleEnabled: false));
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));

        var act = () => service.PreviewAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<FeatureNotAvailableException>();
    }

    [Fact]
    public async Task PreviewAsync_ThrowsForInvalidHorizon()
    {
        var service = CreateService();
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 7, 14), new DateOnly(2026, 4, 14));

        var act = () => service.PreviewAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task PreviewAsync_ThrowsForHorizonExceeding365Days()
    {
        var service = CreateService();
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2027, 2, 1));

        var act = () => service.PreviewAsync(request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*365*");
    }

    [Fact]
    public async Task ApplyAsync_ThrowsOnStaleFingerprint()
    {
        var service = CreateService();
        var request = new AutoScheduleApplyRequest(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14),
            PreviewFingerprint: "stale-fingerprint-that-wont-match");

        var act = () => service.ApplyAsync(request, CancellationToken.None);

        // ConflictException specifically: it is what AppExceptionHandler turns into the 409
        // the apply dialog branches on. A plain InvalidOperationException reached the client
        // as a 500 and the "close and re-run" message never appeared. The mapping itself is
        // covered by the endpoint tests that already assert 409 (criteria, person
        // allocation), so pinning the type here is what keeps this path honest.
        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*changed since the preview*");
    }

    [Fact]
    public async Task SolveWithFallback_FallsBackToGreedy_WhenPrimarySolverFails()
    {
        var failingSolver = new Mock<ISchedulingSolver>();
        failingSolver.Setup(x => x.Kind).Returns(SolverKind.OrToolsCpSat);
        failingSolver.Setup(x => x.Priority).Returns(100);
        failingSolver
            .Setup(x => x.SolveAsync(It.IsAny<AnalyzedSchedulingProblem>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("OR-Tools failed"));

        var greedySolver = new GreedySchedulingSolver();

        var service = CreateService(solvers: [failingSolver.Object, greedySolver]);
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));

        var result = await service.PreviewAsync(request, CancellationToken.None);

        result.SolverUsed.Should().Be(SolverKind.Greedy);
    }

    [Fact]
    public async Task SolveWithFallback_FallsBackToGreedy_WhenPrimaryReturnsInfeasible()
    {
        var infeasibleSolver = new Mock<ISchedulingSolver>();
        infeasibleSolver.Setup(x => x.Kind).Returns(SolverKind.OrToolsCpSat);
        infeasibleSolver.Setup(x => x.Priority).Returns(100);
        infeasibleSolver
            .Setup(x => x.SolveAsync(It.IsAny<AnalyzedSchedulingProblem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchedulingSolution(
                SolverKind.OrToolsCpSat, SolverStatus.Infeasible, [], [], []));

        var greedySolver = new GreedySchedulingSolver();

        var service = CreateService(solvers: [infeasibleSolver.Object, greedySolver]);
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14));

        var result = await service.PreviewAsync(request, CancellationToken.None);

        result.SolverUsed.Should().Be(SolverKind.Greedy);
    }

    [Fact]
    public async Task Preview_OmittedTypes_ResolveToEveryActiveTypeARequestCanTarget()
    {
        // Every active type except people: they are attached on the request's own tab, many
        // per request, and are not a slot the solver fills. The fingerprint is a hash namespaced
        // by the resolved set, so the same solve under a different set must fingerprint
        // differently — that is what proves the resolution reached it.
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        var shop = await CreateService(types: [Type("mill"), Type("saw"), Type("person", directory: true), Type("lathe", active: false)])
            .PreviewAsync(request, CancellationToken.None);
        var millsOnly = await CreateService(types: [Type("mill")])
            .PreviewAsync(request, CancellationToken.None);
        var sawsAndMills = await CreateService(types: [Type("saw"), Type("mill")])
            .PreviewAsync(request, CancellationToken.None);

        Assert.NotEqual(millsOnly.Fingerprint, shop.Fingerprint);
        Assert.Equal(sawsAndMills.Fingerprint, shop.Fingerprint);
    }

    [Fact]
    public async Task Preview_GivenTypes_RejectsOneThatIsUnknownOrInactive()
    {
        var service = CreateService(types: [Type("space"), Type("lathe", active: false)]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(
            new AutoSchedulePreviewRequest(Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                ResourceTypeKeys: ["space", "lathe", "cnc"]),
            CancellationToken.None));

        Assert.Contains("cnc, lathe", ex.Message);
    }

    [Fact]
    public async Task Preview_OmittedTypes_WithNoSchedulableTypes_SaysSo()
    {
        var service = CreateService(types: [Type("person", directory: true)]);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(
            new AutoSchedulePreviewRequest(Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))),
            CancellationToken.None));
    }

    /// <summary>Returns one fixed placement, so a test can pin the window that reaches the write.</summary>
    private sealed class StubSolver(int start, int end, Guid requestId, IReadOnlyList<PlacedResource> resources)
        : ISchedulingSolver
    {
        public SolverKind Kind => SolverKind.Greedy;
        public int Priority => 999;

        public Task<SchedulingSolution> SolveAsync(
            AnalyzedSchedulingProblem problem, CancellationToken cancellationToken)
            => Task.FromResult(new SchedulingSolution(
                SolverKind.Greedy, SolverStatus.Optimal,
                [new ScheduledPlacement(requestId, resources, start, end,
                    DurationMinutes: end - start, Priority: 1)],
                [], []));
    }

    private async Task<PlacementWrite> ApplyAndCaptureWindowAsync(
        int start, int end, WorkingTimeAxis? axis = null, IReadOnlyList<PlacedResource>? resources = null)
    {
        var requestId = Guid.NewGuid();
        List<PlacementWrite>? captured = null;
        var repo = new Mock<IRequestRepository>();
        repo.Setup(r => r.BatchApplyPlacementsAsync(
                It.IsAny<IReadOnlyList<PlacementWrite>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<PlacementWrite>, CancellationToken>((u, _) => captured = [.. u])
            .ReturnsAsync(1);

        var service = CreateService(
            solvers: [new StubSolver(start, end, requestId, resources ?? [new PlacedResource("space", Guid.NewGuid())])],
            requestRepository: repo.Object,
            axis: axis);

        await service.ApplyAsync(
            new AutoScheduleApplyRequest(Guid.NewGuid(), new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14)),
            CancellationToken.None);

        captured.Should().NotBeNull();
        return captured!.Single();
    }

    [Fact]
    public async Task ApplyAsync_WritesEveryResourceThePlacementNamed()
    {
        var room = Guid.NewGuid();
        var van = Guid.NewGuid();

        var write = await ApplyAndCaptureWindowAsync(0, 60,
            resources: [new PlacedResource("space", room), new PlacedResource("van", van)]);

        write.ResourceIds.Should().BeEquivalentTo([room, van]);
    }

    [Fact]
    public async Task ApplyAsync_WritesThePlacementAsAHalfOpenTimestampWindow()
    {
        // Offsets on the identity axis are minutes since the horizon start, so a placement of
        // [0, 1440) is the first horizon day, written as [14 Apr 00:00, 15 Apr 00:00).
        var window = await ApplyAndCaptureWindowAsync(0, 1440);

        window.StartTs.Should().Be(new DateTime(2026, 4, 14, 0, 0, 0, DateTimeKind.Utc));
        window.EndTs.Should().Be(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ApplyAsync_WritesASubDayPlacementToTheMinute()
    {
        var window = await ApplyAndCaptureWindowAsync(8 * 60 + 20, 8 * 60 + 20 + 180);

        window.StartTs.Should().Be(new DateTime(2026, 4, 14, 8, 20, 0, DateTimeKind.Utc));
        window.EndTs.Should().Be(new DateTime(2026, 4, 14, 11, 20, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ApplyAsync_SnapsAWorkingDayPlacementInsideTheWorkingDay()
    {
        // 14 April 2026 is a Tuesday. On a 08:00–17:00 axis the second working day is offsets
        // [540, 1080): its start is Wednesday 08:00 and its end is Wednesday 17:00 — not
        // Thursday 08:00, which names the same offset from the other side of the night.
        var axis = WorkingTimeAxis.Build(new DateOnly(2026, 4, 14), new DateOnly(2026, 7, 14),
            SchedulingSettingsInfo.Default(Guid.NewGuid()) with
            {
                WorkingHoursEnabled = true,
                WorkingDayStart = new TimeOnly(8, 0),
                WorkingDayEnd = new TimeOnly(17, 0),
            }, respectSchedulingSettings: true);

        var window = await ApplyAndCaptureWindowAsync(540, 1080, axis);

        window.StartTs.Should().Be(new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc));
        window.EndTs.Should().Be(new DateTime(2026, 4, 15, 17, 0, 0, DateTimeKind.Utc));
    }
}

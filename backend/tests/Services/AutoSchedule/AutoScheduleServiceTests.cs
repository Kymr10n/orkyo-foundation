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

    private static AutoScheduleService CreateService(
        IFeatureGate? featureGate = null,
        TenantSettings? settings = null,
        IEnumerable<ISchedulingSolver>? solvers = null,
        IReadOnlyList<string>? placeableKeys = null,
        IReadOnlyList<WithheldRequestNode>? withheld = null)
    {
        var mockProblemBuilder = new Mock<SchedulingProblemBuilder>(
            Mock.Of<IRequestRepository>(),
            Mock.Of<IResourceRepository>(),
            Mock.Of<IResourceCapabilityRepository>(),
            Mock.Of<ISchedulingRepository>(),
            Mock.Of<IAvailabilityResolver>(),
            Mock.Of<IRequestDependencyRepository>());

        var problem = new SchedulingProblem(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 14),
            new DateOnly(2026, 7, 14),
            [], [], [], null, null, null, withheld);

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

        // A single placeable type by default, so an omitted resourceTypeKey resolves cleanly.
        var typeRepo = new Mock<IResourceTypeRepository>();
        typeRepo.Setup(r => r.GetPlaceableKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(placeableKeys ?? ["space"]);

        return new AutoScheduleService(
            mockProblemBuilder.Object,
            analyzer,
            resolvedSolvers,
            Mock.Of<IRequestRepository>(),
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
            ResourceTypeKey: ResourceTypeKeys.Space);

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
    public async Task Preview_OmittedType_ResolvesTheSinglePlaceableType()
    {
        // The old behaviour was a hardcoded `?? space` applied twice, independently. Now it is
        // one resolution: the tenant's only placeable type, whatever its key. The fingerprint is
        // a hash namespaced by the resolved type, so the same solve under a different resolved
        // type must fingerprint differently — that is what proves the resolution reached it.
        var request = new AutoSchedulePreviewRequest(Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        var boothTenant = await CreateService(placeableKeys: ["booth"])
            .PreviewAsync(request, CancellationToken.None);
        var spaceTenant = await CreateService(placeableKeys: ["space"])
            .PreviewAsync(request, CancellationToken.None);

        Assert.NotEqual(spaceTenant.Fingerprint, boothTenant.Fingerprint);
    }

    [Fact]
    public async Task Preview_OmittedType_WithSeveralPlaceableTypes_RefusesToGuess()
    {
        // "Which pool?" has no single answer for a tenant with two placeable types, and guessing
        // one means silently solving for the wrong machines.
        var service = CreateService(placeableKeys: ["space", "mill"]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(
            new AutoSchedulePreviewRequest(Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))),
            CancellationToken.None));

        Assert.Contains("resourceTypeKey", ex.Message);
    }

    [Fact]
    public async Task Preview_OmittedType_WithNoPlaceableTypes_SaysSo()
    {
        var service = CreateService(placeableKeys: []);

        await Assert.ThrowsAsync<ArgumentException>(() => service.PreviewAsync(
            new AutoSchedulePreviewRequest(Guid.NewGuid(),
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7))),
            CancellationToken.None));
    }
}

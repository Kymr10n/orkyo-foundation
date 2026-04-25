using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Api.Services.AutoSchedule;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkyo.Foundation.Tests.Services.AutoSchedule;

public class AutoScheduleServiceTests
{
    private static TenantContext MakeTenantContext(ServiceTier tier = ServiceTier.Professional)
        => new()
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "test",
            TenantDbConnectionString = "",
            Tier = tier,
            Status = "active",
        };

    private static TenantSettings MakeSettings(bool autoScheduleEnabled = true)
        => new() { AutoSchedule_Enabled = autoScheduleEnabled };

    private static AutoScheduleService CreateService(
        TenantContext? tenantContext = null,
        TenantSettings? settings = null,
        IEnumerable<ISchedulingSolver>? solvers = null)
    {
        var mockProblemBuilder = new Mock<SchedulingProblemBuilder>(
            Mock.Of<IRequestRepository>(),
            Mock.Of<ISpaceRepository>(),
            Mock.Of<ISpaceCapabilityRepository>(),
            Mock.Of<ISchedulingRepository>());

        var problem = new SchedulingProblem(
            Guid.NewGuid(),
            new DateOnly(2026, 4, 14),
            new DateOnly(2026, 7, 14),
            [], [], [], null, null,
            AutoScheduleMode.FillGapsOnly);

        mockProblemBuilder
            .Setup(x => x.BuildAsync(It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(problem);

        var analyzer = new SchedulingFeasibilityAnalyzer();
        var resolvedSolvers = solvers ?? [new GreedySchedulingSolver()];

        var mockSettingsService = new Mock<ITenantSettingsService>();
        mockSettingsService
            .Setup(x => x.GetSettingsAsync())
            .ReturnsAsync(settings ?? MakeSettings());

        return new AutoScheduleService(
            mockProblemBuilder.Object,
            analyzer,
            resolvedSolvers,
            Mock.Of<IRequestRepository>(),
            tenantContext ?? MakeTenantContext(),
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
    public async Task PreviewAsync_ThrowsForFreeTier()
    {
        var service = CreateService(tenantContext: MakeTenantContext(ServiceTier.Free));
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

        await act.Should().ThrowAsync<InvalidOperationException>()
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
}

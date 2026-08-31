using Api.Helpers;
using Api.Models;
using Api.PlatformApi.Mcp;
using Api.Security;
using Api.Services;
using Api.Services.AutoSchedule;
using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using ModelContextProtocol;
using Moq;
using Xunit;
// Api.Models also defines a ValidationResult; FluentValidation's is the one the
// validators actually return.
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The one tool pair that can change many placements at once, and therefore the one with a real
/// safety contract. The tests that matter here are the contract's: preview echoes the arguments
/// that commit it, apply refuses a stale plan without writing, and neither reaches the solver when
/// the caller lacks the scope or the tenant lacks the feature.
/// </summary>
public class AutoScheduleToolsTests
{
    private readonly Mock<IAutoScheduleService> _service = new();
    private readonly Mock<IValidator<AutoSchedulePreviewRequest>> _previewValidator = new();
    private readonly Mock<IValidator<AutoScheduleApplyRequest>> _applyValidator = new();

    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 6, 1);
    private static readonly DateOnly End = new(2026, 6, 30);
    private const string Fingerprint = "abc123fingerprint";

    public AutoScheduleToolsTests()
    {
        _previewValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _applyValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AutoScheduleApplyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private AutoScheduleTools CreateTools(
        TenantRole role = TenantRole.Editor, McpSolveThrottle? throttle = null)
    {
        var tenant = new CurrentTenant();
        tenant.SetContext(new TenantContext
        {
            TenantId = Guid.NewGuid(),
            TenantSlug = "acme",
            TenantDbConnectionString = "Host=localhost;Database=t",
            Status = "active",
        });

        return new AutoScheduleTools(
            _service.Object, _previewValidator.Object, _applyValidator.Object,
            throttle ?? new McpSolveThrottle(),
            McpToolTestContext.ForRole(role), tenant);
    }

    private static AutoSchedulePreviewResponse Plan(string fingerprint = Fingerprint) => new(
        SolverKind.OrToolsCpSat,
        SolverStatus.Optimal,
        new AutoScheduleScore(3, 1, 30),
        [new ProposedAssignmentDto(Guid.NewGuid(), "Mill", Guid.NewGuid(), "Bench 1", Start, End, 2)],
        [new UnscheduledRequestDto(Guid.NewGuid(), "Weld", [SchedulingReasonCode.NoCompatibleResource])],
        ["solved in 1.2s"],
        fingerprint);

    private void SetupPreview(AutoSchedulePreviewResponse? plan = null) =>
        _service.Setup(s => s.PreviewAsync(It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(plan ?? Plan());

    // ── preview ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Preview_PassesTheHorizonAndTypeThrough()
    {
        SetupPreview();

        await CreateTools().PreviewAsync(SiteId, Start, End, resourceTypeKey: "machine");

        _service.Verify(s => s.PreviewAsync(It.Is<AutoSchedulePreviewRequest>(r =>
            r.SiteId == SiteId && r.HorizonStart == Start && r.HorizonEnd == End
            && r.ResourceTypeKey == "machine"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Preview_EchoesTheArgumentsThatCommitThePlan()
    {
        // ApplyAsync re-solves from whatever it is handed, so a fingerprint paired with a drifted
        // argument yields a different solve and a stale-plan refusal the agent would misread as
        // "the data changed". Echoing removes that whole failure class.
        SetupPreview();
        var requestIds = new[] { Guid.NewGuid() };

        var result = await CreateTools().PreviewAsync(
            SiteId, Start, End, requestIds, "machine", respectSchedulingSettings: false);

        var echo = result.ApplyArguments;
        echo.SiteId.Should().Be(SiteId);
        echo.HorizonStart.Should().Be(Start);
        echo.HorizonEnd.Should().Be(End);
        echo.RequestIds.Should().BeEquivalentTo(requestIds);
        echo.ResourceTypeKey.Should().Be("machine");
        echo.RespectSchedulingSettings.Should().BeFalse();
        echo.PreviewFingerprint.Should().Be(Fingerprint);
    }

    [Fact]
    public async Task Preview_ReturnsTheSolverPlanUnchanged()
    {
        // Composed around the solver's own response rather than restated, so reason codes and
        // diagnostics reach the agent without a second mapping to keep in step.
        var plan = Plan();
        SetupPreview(plan);

        var result = await CreateTools().PreviewAsync(SiteId, Start, End);

        result.Plan.Should().BeSameAs(plan);
        result.Plan.Unscheduled.Single().ReasonCodes
            .Should().Contain(SchedulingReasonCode.NoCompatibleResource);
    }

    [Fact]
    public async Task Preview_IsAllowedForAReadOnlyToken()
    {
        // Matches the HTTP endpoint, which carries AllowMemberWrite because preview persists
        // nothing. A read-only agent drafting a plan for a human is a case worth keeping.
        SetupPreview();

        var result = await CreateTools(TenantRole.Viewer).PreviewAsync(SiteId, Start, End);

        result.Plan.Should().NotBeNull();
    }

    [Fact]
    public async Task Preview_RefusesAnInvalidRequestBeforeSolving()
    {
        // MCP bypasses EndpointHelpers.ExecuteAsync, so without the explicit bridge this validator
        // would silently never run.
        _previewValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("HorizonEnd", "Horizon end must follow start.")]));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().PreviewAsync(SiteId, End, Start));

        thrown.Message.Should().Contain("Horizon end must follow start.");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Preview_TranslatesAMissingFeatureIntoAPlainRefusal()
    {
        // Untranslated this reads to a model as a server fault worth retrying forever.
        _service.Setup(s => s.PreviewAsync(It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureNotAvailableException("Auto-Schedule", "A tenant administrator can enable it in Settings > Configuration."));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().PreviewAsync(SiteId, Start, End));

        // The reason is passed through, not flattened: auto-scheduling is gated by BOTH a plan
        // entitlement and a tenant setting, and an admin whose only problem is the setting must
        // not be told to upgrade.
        thrown.Message.Should().Contain("unavailable");
        thrown.Message.Should().Contain("enable it in Settings");
    }

    [Fact]
    public async Task Preview_IsRefusedWhenTheSolverIsSaturated()
    {
        using var throttle = new McpSolveThrottle(maxConcurrentSolves: 1, solvesPerTenantPerMinute: 100);
        using var held = await throttle.AcquireAsync(Guid.NewGuid());
        SetupPreview();

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(throttle: throttle).PreviewAsync(SiteId, Start, End));

        thrown.Message.Should().Contain("busy");
        _service.VerifyNoOtherCalls();
    }

    // ── apply ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_ForwardsTheFingerprintToTheService()
    {
        _service.Setup(s => s.ApplyAsync(It.IsAny<AutoScheduleApplyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AutoScheduleApplyResponse(3, 1));

        var result = await CreateTools().ApplyAsync(SiteId, Start, End, Fingerprint);

        result.CreatedAssignments.Should().Be(3);
        _service.Verify(s => s.ApplyAsync(It.Is<AutoScheduleApplyRequest>(r =>
            r.PreviewFingerprint == Fingerprint && r.SiteId == SiteId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_RefusesAStalePlanAndSaysNothingWasWritten()
    {
        // The fingerprint check runs before any write, so this is literally true — and the correct
        // next step is one more preview, not a retry of apply.
        _service.Setup(s => s.ApplyAsync(It.IsAny<AutoScheduleApplyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("stale preview"));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().ApplyAsync(SiteId, Start, End, Fingerprint));

        thrown.Message.Should().Contain("nothing was applied");
        thrown.Message.Should().Contain("auto_schedule_preview");
    }

    [Fact]
    public async Task Apply_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).ApplyAsync(SiteId, Start, End, Fingerprint));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Apply_TranslatesAMissingFeatureIntoAPlainRefusal()
    {
        _service.Setup(s => s.ApplyAsync(It.IsAny<AutoScheduleApplyRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FeatureNotAvailableException("auto_schedule", "not included in current plan"));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().ApplyAsync(SiteId, Start, End, Fingerprint));

        thrown.Message.Should().Contain("unavailable");
        thrown.Message.Should().Contain("not included in current plan");
    }

    [Fact]
    public async Task Apply_RefusesAnInvalidRequestBeforeSolving()
    {
        _applyValidator.Setup(v => v.ValidateAsync(
                It.IsAny<AutoScheduleApplyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("SiteId", "Site is required.")]));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().ApplyAsync(Guid.Empty, Start, End, Fingerprint));

        thrown.Message.Should().Contain("Site is required.");
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Preview_SurfacesTheServicesGuidanceWhenTheTypeIsAmbiguous()
    {
        // A tenant with several placeable types must say which one to fill. The service says so
        // precisely, naming the valid keys — that message is the agent's only route to success, so
        // it must not be flattened into a generic failure.
        _service.Setup(s => s.PreviewAsync(It.IsAny<AutoSchedulePreviewRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException(
                "Several placeable resource types exist (cnc, lathe, mill); specify resourceTypeKey."));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().PreviewAsync(SiteId, Start, End));

        thrown.Message.Should().Contain("specify resourceTypeKey");
        thrown.Message.Should().Contain("cnc, lathe, mill");
    }
}

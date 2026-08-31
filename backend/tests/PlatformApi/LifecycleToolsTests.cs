using Api.Models;
using Api.PlatformApi.Mcp;
using Api.Repositories;
using Api.Security;
using Api.Services;
using AwesomeAssertions;
using FluentValidation;
using FluentValidation.Results;
using ModelContextProtocol;
using Moq;
using Xunit;
// Api.Models also defines a ValidationResult; FluentValidation's is what validators return.
using ValidationResult = FluentValidation.Results.ValidationResult;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The tools that add work and take capacity away — the widest blast radius in the MCP surface,
/// and the reason the containment decisions here need tests rather than comments.
/// </summary>
public class LifecycleToolsTests
{
    private readonly Mock<IRequestService> _requests = new();
    private readonly Mock<IRequestDependencyService> _dependencies = new();
    private readonly Mock<IResourceAbsenceRepository> _absences = new();
    private readonly Mock<IValidator<CreateRequestRequest>> _createRequestValidator = new();
    private readonly Mock<IValidator<CreateDependencyRequest>> _dependencyValidator = new();
    private readonly Mock<IValidator<CreateResourceAbsenceRequest>> _absenceValidator = new();

    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid ResourceId = Guid.NewGuid();
    private static readonly Guid AbsenceId = Guid.NewGuid();
    private static readonly DateTime Start = new(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2026, 7, 5, 17, 0, 0, DateTimeKind.Utc);

    public LifecycleToolsTests()
    {
        _createRequestValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _dependencyValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateDependencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        _absenceValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateResourceAbsenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
    }

    private LifecycleTools CreateTools(TenantRole role = TenantRole.Editor) =>
        new(_requests.Object, _dependencies.Object, _absences.Object,
            _createRequestValidator.Object, _dependencyValidator.Object, _absenceValidator.Object,
            McpToolTestContext.ForRole(role));

    private static RequestInfo Request() => new()
    {
        Id = RequestId,
        Name = "Deburr the housings",
        PlanningMode = PlanningMode.Leaf,
        Assignments = [],
        TargetResourceTypeKeys = [],
        MinimalDurationValue = 4,
        MinimalDurationUnit = DurationUnit.Hours,
        Status = RequestStatus.New,
        SchedulingSettingsApply = true,
    };

    private static ResourceAbsenceInfo Absence(Guid? resourceId = null) => new()
    {
        Id = AbsenceId,
        ResourceId = resourceId ?? ResourceId,
        AbsenceType = AbsenceType.Maintenance,
        Title = "Annual service",
        StartTs = Start,
        EndTs = End,
        IsRecurring = false,
        Enabled = true,
    };

    // ── create_request ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRequest_CreatesUnscheduledWork()
    {
        // The containment that matters: the model accepts StartTs/EndTs, this tool does not expose
        // them, so an agent cannot place work through the one path that never checks a conflict.
        _requests.Setup(s => s.CreateAsync(It.IsAny<CreateRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Request());

        var result = await CreateTools().CreateRequestAsync("Deburr the housings", 4, DurationUnit.Hours);

        result.IsScheduled.Should().BeFalse();
        _requests.Verify(s => s.CreateAsync(It.Is<CreateRequestRequest>(r =>
            r.Name == "Deburr the housings"
            && r.MinimalDurationValue == 4 && r.MinimalDurationUnit == DurationUnit.Hours
            && r.StartTs == null && r.EndTs == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRequest_PassesTheOptionalPlacementHints()
    {
        var siteId = Guid.NewGuid();
        _requests.Setup(s => s.CreateAsync(It.IsAny<CreateRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Request());

        await CreateTools().CreateRequestAsync("Weld", 2, DurationUnit.Days,
            description: "Frame seams", siteId: siteId, targetResourceTypeKeys: ["machine"]);

        _requests.Verify(s => s.CreateAsync(It.Is<CreateRequestRequest>(r =>
            r.Description == "Frame seams" && r.SiteId == siteId
            && r.TargetResourceTypeKeys!.Contains("machine")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateRequest_RefusesAnInvalidPayloadBeforeWriting()
    {
        _createRequestValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateRequestRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("Name", "Name is required.")]));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().CreateRequestAsync("", 1, DurationUnit.Hours));

        thrown.Message.Should().Contain("Name is required.");
        _requests.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateRequest_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).CreateRequestAsync("X", 1, DurationUnit.Hours));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _requests.VerifyNoOtherCalls();
    }

    // ── link / unlink ────────────────────────────────────────────────────────

    [Fact]
    public async Task LinkRequests_CreatesTheEdgeOnTheSuccessor()
    {
        var predecessor = Guid.NewGuid();
        _dependencies.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateDependencyRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestDependencyInfo
            {
                Id = Guid.NewGuid(),
                PredecessorRequestId = predecessor,
                SuccessorRequestId = RequestId,
                PredecessorName = "Cut",
                SuccessorName = "Weld",
                DependencyType = "finish_to_start",
                LagMinutes = 30,
                CreatedAt = DateTime.UtcNow,
            });

        var result = await CreateTools().LinkRequestsAsync(predecessor, RequestId, lagMinutes: 30);

        result.LagMinutes.Should().Be(30);
        _dependencies.Verify(s => s.CreateAsync(RequestId,
            It.Is<CreateDependencyRequest>(r => r.PredecessorRequestId == predecessor && r.LagMinutes == 30),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LinkRequests_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).LinkRequestsAsync(Guid.NewGuid(), RequestId));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _dependencies.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnlinkRequests_RemovesTheEdge()
    {
        var dependencyId = Guid.NewGuid();
        _dependencies.Setup(s => s.DeleteAsync(RequestId, dependencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        (await CreateTools().UnlinkRequestsAsync(RequestId, dependencyId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnlinkRequests_SaysSoWhenTheEdgeDoesNotExist()
    {
        // A silent "true" would let an agent believe it broke a cycle it did not touch.
        _dependencies.Setup(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().UnlinkRequestsAsync(RequestId, Guid.NewGuid()));

        thrown.Message.Should().Contain("list_dependencies");
    }

    [Fact]
    public async Task UnlinkRequests_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).UnlinkRequestsAsync(RequestId, Guid.NewGuid()));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _dependencies.VerifyNoOtherCalls();
    }

    // ── absences ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAbsences_IsAllowedForAReadOnlyToken()
    {
        _absences.Setup(s => s.GetByResourceAsync(ResourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Absence()]);

        var result = await CreateTools(TenantRole.Viewer).ListResourceAbsencesAsync(ResourceId);

        result.Should().ContainSingle(a => a.Title == "Annual service");
    }

    [Fact]
    public async Task BlockResourceTime_CreatesTheAbsence()
    {
        _absences.Setup(s => s.CreateAsync(It.IsAny<Guid>(), It.IsAny<CreateResourceAbsenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Absence());

        var result = await CreateTools().BlockResourceTimeAsync(
            ResourceId, AbsenceType.Maintenance, "Annual service", Start, End);

        result.Id.Should().Be(AbsenceId);
        _absences.Verify(s => s.CreateAsync(ResourceId, It.Is<CreateResourceAbsenceRequest>(r =>
            r.AbsenceType == AbsenceType.Maintenance && r.Title == "Annual service"
            && r.StartTs == Start && r.EndTs == End), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BlockResourceTime_RefusesAnInvalidPeriodBeforeWriting()
    {
        _absenceValidator.Setup(v => v.ValidateAsync(It.IsAny<CreateResourceAbsenceRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult([new ValidationFailure("EndTs", "End must be after start.")]));

        var thrown = await Assert.ThrowsAsync<McpException>(() => CreateTools()
            .BlockResourceTimeAsync(ResourceId, AbsenceType.Maintenance, "Bad", End, Start));

        thrown.Message.Should().Contain("End must be after start.");
        _absences.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BlockResourceTime_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(() => CreateTools(TenantRole.Viewer)
            .BlockResourceTimeAsync(ResourceId, AbsenceType.Maintenance, "X", Start, End));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _absences.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnblockResourceTime_RemovesAnAbsenceThatBelongsToTheResource()
    {
        _absences.Setup(s => s.GetByIdAsync(AbsenceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Absence());
        _absences.Setup(s => s.DeleteAsync(AbsenceId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await CreateTools().UnblockResourceTimeAsync(ResourceId, AbsenceId)).Should().BeTrue();
    }

    [Fact]
    public async Task UnblockResourceTime_WillNotDeleteAnotherResourcesAbsence()
    {
        // The ownership re-check is what stops a hallucinated id from freeing up a machine nobody
        // asked about. Without it the delete would succeed on the id alone.
        _absences.Setup(s => s.GetByIdAsync(AbsenceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Absence(resourceId: Guid.NewGuid()));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().UnblockResourceTimeAsync(ResourceId, AbsenceId));

        thrown.Message.Should().Contain("list_resource_absences");
        _absences.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnblockResourceTime_SaysSoWhenTheAbsenceDoesNotExist()
    {
        _absences.Setup(s => s.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ResourceAbsenceInfo?)null);

        await Assert.ThrowsAsync<McpException>(
            () => CreateTools().UnblockResourceTimeAsync(ResourceId, AbsenceId));

        _absences.Verify(s => s.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UnblockResourceTime_IsRefusedForAReadOnlyToken()
    {
        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools(TenantRole.Viewer).UnblockResourceTimeAsync(ResourceId, AbsenceId));

        thrown.Message.Should().Contain(PlatformApiScopes.ScheduleWrite);
        _absences.VerifyNoOtherCalls();
    }
}

using Api.Constants;
using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// What a routing turns into when it is instantiated, and the rules that keep a routing
/// schedulable: every step is a request template, and at instantiation every step targets a
/// resource type. Persistence is mocked; the chain write itself is the repository's.
/// </summary>
public class RoutingServiceTests
{
    private readonly Mock<IRoutingRepository> _routings = new();
    private readonly Mock<ITemplateRepository> _templates = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly Mock<IRequestService> _requestService = new();
    private readonly RoutingService _service;

    private static readonly Guid RoutingId = Guid.NewGuid();
    private static readonly Guid SawId = Guid.NewGuid();
    private static readonly Guid MillId = Guid.NewGuid();
    private static readonly Guid DeburrId = Guid.NewGuid();
    private static readonly Guid Tolerance = Guid.NewGuid();

    public RoutingServiceTests()
    {
        _service = new RoutingService(_routings.Object, _templates.Object, _requests.Object, _requestService.Object);

        Template("Saw", SawId, "saw");
        Template("Mill", MillId, "mill", items: [(Tolerance, "0.05")]);
        Template("Deburr", DeburrId, "bench");

        _routings.Setup(r => r.GetByIdAsync(RoutingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Bracket());

        _requests.Setup(r => r.CreateChainAsync(
                It.IsAny<CreateRequestRequest>(), It.IsAny<IReadOnlyList<CreateRequestRequest>>(),
                It.IsAny<IReadOnlyList<ChainEdge>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateRequestRequest p, IReadOnlyList<CreateRequestRequest> c, IReadOnlyList<ChainEdge> _, CancellationToken _) =>
                (new RequestInfo
                {
                    Id = Guid.NewGuid(),
                    Name = p.Name,
                    PlanningMode = p.PlanningMode,
                    MinimalDurationValue = p.MinimalDurationValue,
                    MinimalDurationUnit = p.MinimalDurationUnit,
                    Status = RequestStatus.New,
                    SchedulingSettingsApply = true,
                    Assignments = [],
                    TargetResourceTypeKeys = [],
                }, c.Select(_ => Guid.NewGuid()).ToList()));
    }

    private void Template(string name, Guid id, string? typeKey, string entityType = TemplateEntityTypes.Request,
        IReadOnlyList<(Guid CriterionId, string Value)>? items = null)
    {
        _templates.Setup(t => t.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Template
            {
                Id = id,
                Name = name,
                EntityType = entityType,
                TargetResourceTypeKeys = typeKey is null ? [] : [typeKey],
            });
        _templates.Setup(t => t.GetTemplateItemsAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items ?? []).Select(i => new TemplateItem
            {
                Id = Guid.NewGuid(),
                TemplateId = id,
                CriterionId = i.CriterionId,
                Value = i.Value,
            }).ToList());
    }

    /// <summary>Saw 5 + 5/unit → Mill 30 + 60/unit, 4 h lag after → Deburr 0 + 15/unit.</summary>
    private static RoutingInfo Bracket() => new()
    {
        Id = RoutingId,
        Name = "Bracket",
        Steps =
        [
            Step(1, SawId, "Saw", setup: 5, run: 5, lag: 0),
            Step(2, MillId, "Mill", setup: 30, run: 60, lag: 240),
            Step(3, DeburrId, "Deburr", setup: 0, run: 15, lag: 0),
        ],
    };

    private static RoutingStepInfo Step(int no, Guid templateId, string name, int setup, int run, int lag) => new()
    {
        Id = Guid.NewGuid(),
        StepNo = no,
        OperationTemplateId = templateId,
        OperationName = name,
        SetupMinutes = setup,
        RunMinutesPerUnit = run,
        LagMinutesAfter = lag,
    };

    private static InstantiateRoutingRequest Order(int quantity = 4, Guid? parent = null) => new()
    {
        Name = "WO-1001",
        Quantity = quantity,
        SiteId = Guid.NewGuid(),
        EarliestStartTs = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        LatestEndTs = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
        ParentRequestId = parent,
    };

    private (CreateRequestRequest Parent, IReadOnlyList<CreateRequestRequest> Children, IReadOnlyList<ChainEdge> Edges) Captured()
    {
        var call = _requests.Invocations.Single(i => i.Method.Name == nameof(IRequestRepository.CreateChainAsync));
        return ((CreateRequestRequest)call.Arguments[0],
            (IReadOnlyList<CreateRequestRequest>)call.Arguments[1],
            (IReadOnlyList<ChainEdge>)call.Arguments[2]);
    }

    [Fact]
    public async Task Instantiate_MakesOneLeafPerStep_LastingSetupPlusRunTimesQuantity()
    {
        await _service.InstantiateAsync(RoutingId, Order(quantity: 4));

        var (_, children, _) = Captured();
        children.Select(c => c.Name).Should().Equal("1. Saw", "2. Mill", "3. Deburr");
        children.Select(c => c.MinimalDurationValue).Should().Equal(5 + 5 * 4, 30 + 60 * 4, 15 * 4);
        children.Should().OnlyContain(c => c.MinimalDurationUnit == DurationUnit.Minutes);
        children.Should().OnlyContain(c => c.PlanningMode == PlanningMode.Leaf);
        children.Select(c => c.SortOrder).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Instantiate_CopiesTheOperationsTargetTypesAndRequirements()
    {
        await _service.InstantiateAsync(RoutingId, Order());

        var (_, children, _) = Captured();
        children[0].TargetResourceTypeKeys.Should().BeEquivalentTo(["saw"]);
        children[1].TargetResourceTypeKeys.Should().BeEquivalentTo(["mill"]);
        var requirement = children[1].Requirements.Should().ContainSingle().Subject;
        requirement.CriterionId.Should().Be(Tolerance);
        requirement.Value.GetDouble().Should().Be(0.05);
        children[2].Requirements.Should().BeEmpty();
    }

    [Fact]
    public async Task Instantiate_ChainsTheStepsFinishToStart_WithEachStepsLag()
    {
        await _service.InstantiateAsync(RoutingId, Order());

        var (_, _, edges) = Captured();
        edges.Should().Equal(new ChainEdge(0, 1, 0), new ChainEdge(1, 2, 240));
    }

    [Fact]
    public async Task Instantiate_MakesAContainerCarryingTheJobWindow_AndCopiesItToEveryLeaf()
    {
        var order = Order();

        await _service.InstantiateAsync(RoutingId, order);

        var (parent, children, _) = Captured();
        parent.Name.Should().Be("WO-1001");
        parent.PlanningMode.Should().Be(PlanningMode.Container);
        parent.SiteId.Should().Be(order.SiteId);
        parent.EarliestStartTs.Should().Be(order.EarliestStartTs);
        parent.LatestEndTs.Should().Be(order.LatestEndTs);
        parent.TargetResourceTypeKeys.Should().BeEmpty("a container holds no assignments of its own");
        children.Should().OnlyContain(c => c.EarliestStartTs == order.EarliestStartTs && c.LatestEndTs == order.LatestEndTs);
        children.Should().OnlyContain(c => c.SiteId == order.SiteId);
    }

    [Fact]
    public async Task Instantiate_UnderAParent_ChecksTheParentCanTakeChildren()
    {
        var parent = Guid.NewGuid();
        _requestService.Setup(s => s.EnsureCanParentAsync(parent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("Cannot add children to a leaf request"));

        await Assert.ThrowsAsync<ConflictException>(() => _service.InstantiateAsync(RoutingId, Order(parent: parent)));

        _requests.Verify(r => r.CreateChainAsync(
            It.IsAny<CreateRequestRequest>(), It.IsAny<IReadOnlyList<CreateRequestRequest>>(),
            It.IsAny<IReadOnlyList<ChainEdge>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Instantiate_RefusesAStepWhoseOperationTargetsNoType_AndWritesNothing()
    {
        // An operation with no resource type cannot be scheduled; a job with such a step would
        // sit in the backlog forever with no reason it can act on.
        Template("Inspect", DeburrId, typeKey: null);

        var thrown = await Assert.ThrowsAsync<ConflictException>(() => _service.InstantiateAsync(RoutingId, Order()));

        thrown.Message.Should().Contain("Step 3").And.Contain("Inspect");
        _requests.Verify(r => r.CreateChainAsync(
            It.IsAny<CreateRequestRequest>(), It.IsAny<IReadOnlyList<CreateRequestRequest>>(),
            It.IsAny<IReadOnlyList<ChainEdge>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Instantiate_UnknownRouting_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.InstantiateAsync(Guid.NewGuid(), Order()));
    }

    [Fact]
    public async Task Create_RefusesAStepThatIsNotARequestTemplate()
    {
        var space = Guid.NewGuid();
        Template("Meeting room", space, "space", entityType: TemplateEntityTypes.Space);

        var thrown = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(new CreateRoutingRequest
        {
            Name = "Bad",
            Steps = [new RoutingStepRequest { StepNo = 1, OperationTemplateId = space, RunMinutesPerUnit = 10 }],
        }));

        thrown.Message.Should().Contain("space template");
        _routings.Verify(r => r.CreateAsync(It.IsAny<CreateRoutingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_RefusesAStepWhoseOperationDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.UpdateAsync(RoutingId, new UpdateRoutingRequest
        {
            Name = "Bracket",
            Steps = [new RoutingStepRequest { StepNo = 1, OperationTemplateId = Guid.NewGuid(), RunMinutesPerUnit = 10 }],
        }));
    }

    [Fact]
    public async Task Create_WithRequestTemplates_Writes()
    {
        var request = new CreateRoutingRequest
        {
            Name = "Bracket",
            Steps = [new RoutingStepRequest { StepNo = 1, OperationTemplateId = SawId, RunMinutesPerUnit = 10 }],
        };
        _routings.Setup(r => r.CreateAsync(request, It.IsAny<CancellationToken>())).ReturnsAsync(Bracket());

        var created = await _service.CreateAsync(request);

        created.Name.Should().Be("Bracket");
    }

    // ── CRUD passes through once the operations check out ────────────

    [Fact]
    public async Task GetAllAndGetById_PassThroughToTheRepository()
    {
        _routings.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Bracket()]);

        (await _service.GetAllAsync()).Should().ContainSingle(r => r.Id == RoutingId);
        (await _service.GetByIdAsync(RoutingId))!.Id.Should().Be(RoutingId);
    }

    [Fact]
    public async Task Delete_PassesThroughToTheRepository()
    {
        _routings.Setup(r => r.DeleteAsync(RoutingId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        (await _service.DeleteAsync(RoutingId)).Should().BeTrue();
    }

    [Fact]
    public async Task Update_WithRequestTemplates_WritesTheRouting()
    {
        var request = new UpdateRoutingRequest
        {
            Name = "Bracket v2",
            Steps = [new RoutingStepRequest { StepNo = 1, OperationTemplateId = MillId, RunMinutesPerUnit = 45 }],
        };
        _routings.Setup(r => r.UpdateAsync(RoutingId, request, It.IsAny<CancellationToken>())).ReturnsAsync(Bracket());

        var updated = await _service.UpdateAsync(RoutingId, request);

        updated.Should().NotBeNull();
        _routings.Verify(r => r.UpdateAsync(RoutingId, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithASpaceTemplate_IsRefusedBeforeAnyWrite()
    {
        var roomId = Guid.NewGuid();
        Template("Room", roomId, null, entityType: TemplateEntityTypes.Space);
        var request = new UpdateRoutingRequest
        {
            Name = "Bracket",
            Steps = [new RoutingStepRequest { StepNo = 1, OperationTemplateId = roomId, RunMinutesPerUnit = 5 }],
        };

        var act = () => _service.UpdateAsync(RoutingId, request);

        await act.Should().ThrowAsync<ConflictException>().WithMessage("*space template, not an operation*");
        _routings.Verify(r => r.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdateRoutingRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

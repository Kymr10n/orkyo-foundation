using Api.Helpers;
using Api.Models;
using Api.Models.Insights;
using Api.PlatformApi.Mcp;
using Api.Services;
using Api.Services.Insights;
using AwesomeAssertions;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.PlatformApi;

/// <summary>
/// The read-only planning surface. Thin-wrapper tests: correct service call, correct arguments, and
/// the error branches — which for this class means the one case where a service cannot answer at
/// all (a dependency cycle) and the agent must be told what to do instead.
/// </summary>
public class PlanningToolsTests
{
    private readonly Mock<ICriticalPathService> _criticalPath = new();
    private readonly Mock<IRequestDependencyService> _dependencies = new();
    private readonly Mock<IRequestPlanService> _plans = new();
    private readonly Mock<IInsightsService> _insights = new();

    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly DateTime From = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);

    private PlanningTools CreateTools() =>
        new(_criticalPath.Object, _dependencies.Object, _plans.Object, _insights.Object);

    private static RequestDependencyInfo Edge(string predecessor, string successor) => new()
    {
        Id = Guid.NewGuid(),
        PredecessorRequestId = Guid.NewGuid(),
        SuccessorRequestId = Guid.NewGuid(),
        PredecessorName = predecessor,
        SuccessorName = successor,
        DependencyType = "finish_to_start",
        LagMinutes = 0,
        CreatedAt = DateTime.UtcNow,
    };

    // ── get_critical_path ────────────────────────────────────────────────────

    [Fact]
    public async Task CriticalPath_PassesTheSiteThrough()
    {
        _criticalPath.Setup(s => s.ComputeAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CriticalPathResult
            {
                Nodes = [],
                Edges = [],
                DurationDays = 0,
                Diagnostics = [],
            });

        await CreateTools().GetCriticalPathAsync(SiteId);

        _criticalPath.Verify(s => s.ComputeAsync(SiteId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriticalPath_ReturnsTheServiceResultUnchanged()
    {
        // Returned verbatim rather than reprojected — it is already the shape the HTTP endpoint
        // serializes, and float/isCritical are exactly what an agent reasons over.
        var expected = new CriticalPathResult
        {
            Nodes =
            [
                new CriticalPathNode
                {
                    RequestId = RequestId, Name = "Mill the bracket",
                    EarliestStart = new DateOnly(2026, 6, 1),
                    EarliestFinish = new DateOnly(2026, 6, 3),
                    LatestStart = new DateOnly(2026, 6, 1),
                    LatestFinish = new DateOnly(2026, 6, 3),
                    TotalFloatDays = 0, IsCritical = true, IsScheduled = true,
                },
            ],
            Edges = [],
            DurationDays = 12,
            Diagnostics = [],
        };
        _criticalPath.Setup(s => s.ComputeAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await CreateTools().GetCriticalPathAsync();

        result.Should().BeSameAs(expected);
        result.Nodes.Single().IsCritical.Should().BeTrue();
    }

    [Fact]
    public async Task CriticalPath_TranslatesACycleIntoAnActionableRefusal()
    {
        // A cycle makes the answer undefined, so the agent needs the next step named rather than
        // an exception that reads as a server fault worth retrying.
        _criticalPath.Setup(s => s.ComputeAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConflictException("A depends on B which depends on A"));

        var thrown = await Assert.ThrowsAsync<McpException>(
            () => CreateTools().GetCriticalPathAsync());

        thrown.Message.Should().Contain("cycle");
        thrown.Message.Should().Contain("list_dependencies");
        thrown.Message.Should().Contain("A depends on B");
    }

    // ── list_dependencies ────────────────────────────────────────────────────

    [Fact]
    public async Task ListDependencies_WithoutARequest_ListsEveryEdgeInScope()
    {
        _dependencies.Setup(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Edge("Cut", "Weld")]);

        var result = await CreateTools().ListDependenciesAsync(siteId: SiteId);

        result.Should().ContainSingle();
        _dependencies.Verify(s => s.GetAllAsync(SiteId, It.IsAny<CancellationToken>()), Times.Once);
        _dependencies.Verify(s => s.GetForRequestAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListDependencies_ForOneRequest_ReturnsBothDirections()
    {
        // "What is this waiting on?" is almost never asked without "and what waits on it?" — an
        // agent that saw only one side would move work and break the other.
        _dependencies.Setup(s => s.GetForRequestAsync(RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestDependencies
            {
                Predecessors = [Edge("Cut", "Mill")],
                Successors = [Edge("Mill", "Paint")],
            });

        var result = await CreateTools().ListDependenciesAsync(requestId: RequestId);

        result.Should().HaveCount(2);
        result.Select(e => e.PredecessorName).Should().BeEquivalentTo(["Cut", "Mill"]);
        _dependencies.Verify(s => s.GetAllAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListDependencies_ForARequestWithNoEdges_ReturnsEmpty()
    {
        _dependencies.Setup(s => s.GetForRequestAsync(RequestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RequestDependencies { Predecessors = [], Successors = [] });

        (await CreateTools().ListDependenciesAsync(requestId: RequestId)).Should().BeEmpty();
    }

    // ── analyze_capacity ─────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeCapacity_DrivesBothQueriesFromOneFilter()
    {
        // The merge is the point: two tools would let an agent take the ranking without the trend
        // that explains it.
        SetupInsights();

        await CreateTools().AnalyzeCapacityAsync(From, To, SiteId, "machine", "week");

        _insights.Verify(s => s.GetBottlenecksAsync(It.Is<InsightsFilter>(f =>
            f.From == From && f.To == To && f.SiteId == SiteId
            && f.ResourceType == "machine" && f.Bucket == "week"),
            It.IsAny<CancellationToken>()), Times.Once);
        _insights.Verify(s => s.GetUtilizationTrendAsync(It.Is<InsightsFilter>(f =>
            f.From == From && f.To == To && f.SiteId == SiteId
            && f.ResourceType == "machine" && f.Bucket == "week"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnalyzeCapacity_ReturnsBothHalves()
    {
        SetupInsights();

        var result = await CreateTools().AnalyzeCapacityAsync(From, To);

        result.Bottlenecks.Items.Should().ContainSingle(b => b.Name == "CNC Machining");
        result.Trend.Series.Should().ContainSingle();
    }

    private static InsightsMetadata Metadata() =>
        new() { CalculatedAt = DateTime.UtcNow, SourceMode = "live" };

    // ── get_request_plan ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetRequestPlan_ReturnsTheServicesPlan()
    {
        var plan = new RequestPlan
        {
            ParentId = RequestId,
            ParentName = "Line changeover",
            ParentPlanningMode = PlanningMode.Summary,
            Children =
            [
                new RequestPlanChild
                {
                    Id = Guid.NewGuid(), Name = "Purge", PlanningMode = PlanningMode.Leaf,
                    Status = RequestStatus.New, SortOrder = 0,
                    PredecessorLogic = PredecessorLogic.KOfN, PredecessorLogicK = 2,
                    CanStart = false, ExternalPredecessorCount = 1, ExternalSuccessorCount = 0,
                },
            ],
            Edges = [],
        };
        _plans.Setup(p => p.GetPlanAsync(RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

        var result = await CreateTools().GetRequestPlanAsync(RequestId);

        // The service's own record travels to the agent unprojected — the condition and the
        // startability are the whole reason an agent asks.
        result.Should().BeSameAs(plan);
        result.Children.Single().PredecessorLogic.Should().Be(PredecessorLogic.KOfN);
        result.Children.Single().CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task GetRequestPlan_ForAnUnknownRequest_TellsTheAgentPlainly()
    {
        _plans.Setup(p => p.GetPlanAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestPlan?)null);

        var act = () => CreateTools().GetRequestPlanAsync(RequestId);

        // McpException, not a null result: an agent that gets null has nothing to act on.
        (await act.Should().ThrowAsync<McpException>()).Which.Message.Should().Contain("not found");
    }

    private void SetupInsights()
    {
        _insights.Setup(s => s.GetBottlenecksAsync(It.IsAny<InsightsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsBottlenecks
            {
                Period = new InsightsPeriod { From = From, To = To },
                Items =
                [
                    new BottleneckResource
                    {
                        ResourceId = Guid.NewGuid(),
                        Name = "CNC Machining",
                        ResourceTypeKey = "machine",
                        ResourceTypeDisplayName = "Machine",
                        OverbookedMinutes = 480,
                        CapacityMinutes = 2400,
                        PeakUtilizationPercent = 180,
                    },
                ],
                Metadata = Metadata(),
            });

        _insights.Setup(s => s.GetUtilizationTrendAsync(It.IsAny<InsightsFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InsightsUtilization
            {
                ResourceType = "machine",
                Bucket = "week",
                Series =
                [
                    new UtilizationSeriesPoint
                    {
                        BucketStart = From, BucketEnd = To,
                        TotalCapacityMinutes = 2400, UsedCapacityMinutes = 2880,
                        AvailableCapacityMinutes = 0, UtilizationPercent = 120, ConflictCount = 4,
                    },
                ],
                ResourceCount = 3,
                Metadata = Metadata(),
            });
    }
}

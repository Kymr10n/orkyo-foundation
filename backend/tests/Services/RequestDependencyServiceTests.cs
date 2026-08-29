using Api.Helpers;
using Api.Models;
using Api.Repositories;
using Api.Services;
using Moq;
using Xunit;

namespace Api.Tests.Services;

/// <summary>
/// Unit tests for the rules that keep the precedence graph meaningful: leaves only, no
/// self-edges, no duplicates, no cycles. Persistence is mocked — the recursive cycle walk
/// itself is SQL and is covered by the repository/endpoint tests.
/// </summary>
public class RequestDependencyServiceTests
{
    private readonly Mock<IRequestDependencyRepository> _repo = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly RequestDependencyService _service;

    private static readonly Guid Predecessor = Guid.NewGuid();
    private static readonly Guid Successor = Guid.NewGuid();

    public RequestDependencyServiceTests()
    {
        // Both endpoints are schedulable leaves unless a test says otherwise.
        _requests.Setup(r => r.GetPlanningModeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanningMode.Leaf);
        _repo.Setup(r => r.ExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.WouldCreateCycleAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid p, Guid s, string type, int lag, CancellationToken _) => new RequestDependencyInfo
            {
                Id = Guid.NewGuid(),
                PredecessorRequestId = p,
                SuccessorRequestId = s,
                PredecessorName = "Mill",
                SuccessorName = "Grind",
                DependencyType = type,
                LagMinutes = lag
            });

        _service = new RequestDependencyService(_repo.Object, _requests.Object);
    }

    private static CreateDependencyRequest Req(Guid predecessor, int lag = 0) =>
        new() { PredecessorRequestId = predecessor, LagMinutes = lag };

    [Fact]
    public async Task Create_PersistsFinishToStartEdge()
    {
        var created = await _service.CreateAsync(Successor, Req(Predecessor, lag: 90));

        Assert.Equal(Predecessor, created.PredecessorRequestId);
        Assert.Equal(Successor, created.SuccessorRequestId);
        Assert.Equal(DependencyTypes.FinishToStart, created.DependencyType);
        Assert.Equal(90, created.LagMinutes);
    }

    [Fact]
    public async Task Create_SelfEdge_IsRejectedBeforeAnyLookup()
    {
        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Successor, Req(Successor)));

        // A self-edge is decidable without touching the database at all.
        _requests.Verify(r => r.GetPlanningModeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_UnknownRequest_IsNotFound()
    {
        _requests.Setup(r => r.GetPlanningModeAsync(Predecessor, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlanningMode?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(Successor, Req(Predecessor)));
    }

    [Theory]
    [InlineData(PlanningMode.Summary)]
    [InlineData(PlanningMode.Container)]
    public async Task Create_GroupEndpoint_IsRejected(PlanningMode mode)
    {
        // Groups carry rolled-up dates and never reach the scheduler, so an edge on one
        // could not be enforced.
        _requests.Setup(r => r.GetPlanningModeAsync(Predecessor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mode);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Successor, Req(Predecessor)));
        Assert.Contains("Predecessor", ex.Message);
    }

    [Fact]
    public async Task Create_GroupSuccessor_IsRejected()
    {
        _requests.Setup(r => r.GetPlanningModeAsync(Successor, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlanningMode.Summary);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Successor, Req(Predecessor)));
        Assert.Contains("Successor", ex.Message);
    }

    [Fact]
    public async Task Create_DuplicateEdge_IsRejected()
    {
        _repo.Setup(r => r.ExistsAsync(Predecessor, Successor, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Successor, Req(Predecessor)));
        _repo.Verify(r => r.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Cycle_IsRejected()
    {
        _repo.Setup(r => r.WouldCreateCycleAsync(Predecessor, Successor, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Successor, Req(Predecessor)));
        _repo.Verify(r => r.CreateAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_EdgeOfAnotherRequest_IsNotFound()
    {
        var edgeId = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(edgeId, It.IsAny<CancellationToken>())).ReturnsAsync(new RequestDependencyInfo
        {
            Id = edgeId,
            PredecessorRequestId = Guid.NewGuid(),
            SuccessorRequestId = Guid.NewGuid(),
            PredecessorName = "A",
            SuccessorName = "B",
            DependencyType = DependencyTypes.FinishToStart,
            LagMinutes = 0
        });

        // Deleting through an unrelated request means the caller holds a stale graph.
        Assert.False(await _service.DeleteAsync(Successor, edgeId));
        _repo.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_FromEitherEndpoint_Succeeds()
    {
        var edgeId = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(edgeId, It.IsAny<CancellationToken>())).ReturnsAsync(new RequestDependencyInfo
        {
            Id = edgeId,
            PredecessorRequestId = Predecessor,
            SuccessorRequestId = Successor,
            PredecessorName = "Mill",
            SuccessorName = "Grind",
            DependencyType = DependencyTypes.FinishToStart,
            LagMinutes = 0
        });
        _repo.Setup(r => r.DeleteAsync(edgeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True(await _service.DeleteAsync(Predecessor, edgeId));
        Assert.True(await _service.DeleteAsync(Successor, edgeId));
    }

    [Fact]
    public async Task Delete_MissingEdge_IsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RequestDependencyInfo?)null);

        Assert.False(await _service.DeleteAsync(Successor, Guid.NewGuid()));
    }
}

using Api.Models;
using Api.Repositories;
using Api.Services;
using Api.Services.Insights;
using Moq;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// The conflict timeline: joining live conflicts back to when they happen, and to which site.
///
/// This was inside InsightsService, recomputed by every report that needed it — the overview, the
/// conflicts trend, and once per resource type for the utilization charts. It is the same answer
/// for all of them, so it moved behind its own seam; these are the site-filtering rules that came
/// with it.
/// </summary>
public class ConflictTimelineProviderTests
{
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<IRequestRepository> _requests = new();
    private readonly ConflictTimelineProvider _provider;

    private static readonly DateTime Jan = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Mar = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime JanTenth = new(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc);

    public ConflictTimelineProviderTests()
    {
        _provider = new ConflictTimelineProvider(_conflicts.Object, _requests.Object);
    }

    private static ConflictInfo Conflict(string kind) => new()
    {
        Id = $"{Guid.NewGuid()}-{kind}",
        Kind = kind,
        Severity = "error",
        Message = kind,
    };

    private void Conflicted(Guid requestId, DateTime startTs, Guid? siteId, string kind = "overlap")
    {
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RequestConflictInfo
            {
                RequestId = requestId,
                Conflicts = [Conflict(kind)],
            }]);
        _requests.Setup(r => r.GetScheduledLiteAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ScheduledRequestLite(requestId, startTs, siteId)]);
    }

    [Fact]
    public async Task PlacesEachConflictAtItsRequestsScheduledStart()
    {
        // The conflict registry carries no timestamp of its own — the request is what dates it.
        var id = Guid.NewGuid();
        Conflicted(id, JanTenth, siteId: null, kind: "capacity_exceeded");

        var points = await _provider.GetAsync(Jan, Mar, siteId: null);

        var point = Assert.Single(points);
        Assert.Equal(JanTenth, point.StartTs);
        Assert.Equal("capacity_exceeded", point.Kind);
    }

    [Fact]
    public async Task ExcludesConflictsBoundToAnotherSite()
    {
        Conflicted(Guid.NewGuid(), JanTenth, siteId: Guid.NewGuid());

        var points = await _provider.GetAsync(Jan, Mar, siteId: Guid.NewGuid());

        Assert.Empty(points);
    }

    [Fact]
    public async Task KeepsSiteNeutralConflictsUnderAnySite()
    {
        // A request with no site is schedulable anywhere, so it belongs to every site's view.
        Conflicted(Guid.NewGuid(), JanTenth, siteId: null);

        var points = await _provider.GetAsync(Jan, Mar, siteId: Guid.NewGuid());

        Assert.Single(points);
    }

    [Fact]
    public async Task DropsAConflictWhoseRequestIsNotInTheWindow()
    {
        // The registry and the scheduled set are read separately; a conflict with no request to
        // date it cannot be placed on a timeline at all.
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new RequestConflictInfo
            {
                RequestId = Guid.NewGuid(),
                Conflicts = [Conflict("overlap")],
            }]);
        _requests.Setup(r => r.GetScheduledLiteAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        Assert.Empty(await _provider.GetAsync(Jan, Mar, siteId: null));
    }

    [Fact]
    public async Task AsksForNothingElseWhenThereAreNoConflicts()
    {
        // The common case on a healthy workspace: skip the second read entirely.
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        Assert.Empty(await _provider.GetAsync(Jan, Mar, siteId: null));
        _requests.Verify(r => r.GetScheduledLiteAsync(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

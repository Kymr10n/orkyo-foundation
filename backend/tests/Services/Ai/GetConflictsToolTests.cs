using System.Text.Json;
using Api.Models;
using Api.Services;
using Api.Services.Ai;
using Microsoft.Extensions.Time.Testing;

namespace Orkyo.Foundation.Tests.Services.Ai;

/// <summary>
/// The assistant's conflict read used to validate the whole tenant, all-time, on every call, and
/// took any <c>limit</c> the model sent.
/// </summary>
public class GetConflictsToolTests
{
    private static readonly DateTime Now = new(2026, 9, 28, 14, 30, 0, DateTimeKind.Utc);
    private readonly Mock<IConflictService> _conflicts = new();
    private readonly Mock<IRequestService> _requests = new();

    private GetConflictsTool CreateSut(int conflicted)
    {
        var rows = Enumerable.Range(0, conflicted).Select(_ => new RequestConflictInfo
        {
            RequestId = Guid.NewGuid(),
            Conflicts = [new ConflictInfo { Id = "c", Kind = "overlap", Severity = "error", Message = "overlaps" }],
        }).ToList();
        _conflicts.Setup(c => c.GetAllAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _requests.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        return new GetConflictsTool(_conflicts.Object, _requests.Object, new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task ReadsABoundedWindow_NotAllTime()
    {
        await CreateSut(0).ExecuteAsync(JsonDocument.Parse("{}").RootElement, default);

        _conflicts.Verify(c => c.GetAllAsync(
            new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 9, 29, 0, 0, 0, DateTimeKind.Utc),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("{\"limit\": 100000}", 100)]
    [InlineData("{\"limit\": -5}", 1)]
    [InlineData("{}", 25)]
    public async Task TheLimitIsClamped(string input, int reported)
    {
        var result = await CreateSut(150).ExecuteAsync(JsonDocument.Parse(input).RootElement, default);

        _requests.Verify(r => r.GetByIdsAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == reported), false, It.IsAny<CancellationToken>()), Times.Once);
        result.Should().Contain($"({150 - reported} more requests also have conflicts.)");
    }
}

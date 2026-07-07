using System.Net.Http.Json;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Covers <see cref="IRequestRepository.GetPartiallyScheduledLeavesAsync"/> — the eligibility-parity
/// fetch that keeps timed-but-spaceless leaves (start_ts + end_ts set, but no Space assignment, so
/// <see cref="RequestInfo.IsScheduled"/> is false) visible to the auto-scheduler after
/// GetUnscheduledAsync narrowed to start_ts IS NULL. Rows are created over the HTTP stack (real
/// create/schedule paths); the query is exercised against the resolved repository.
/// </summary>
[Collection("Database collection")]
public class RequestRepositoryPartialScheduleTests
{
    private readonly HttpClient _client;
    private readonly IRequestRepository _repo;

    public RequestRepositoryPartialScheduleTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
    }

    private async Task<Guid> CreateLeafAsync(DateTime? startTs, DateTime? endTs)
    {
        var resp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = $"Partial-{Guid.NewGuid():N}"[..25],
            StartTs = startTs,
            EndTs = endTs,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;
    }

    [Fact]
    public async Task GetPartiallyScheduledLeaves_IncludesTimedButSpaceless_ExcludesUnscheduledAndFullyScheduled()
    {
        var start = DateTime.UtcNow.Date.AddDays(5).AddHours(9);
        var end = start.AddHours(2);

        // A: timed (start + end) but NO space assignment — the "timed-but-spaceless" partial case
        // that GetUnscheduledAsync (start_ts IS NULL) drops but the solver must still see.
        var timedSpacelessId = await CreateLeafAsync(start, end);

        // B: unscheduled (start_ts IS NULL) — belongs to GetUnscheduledAsync, must NOT appear here.
        var unscheduledId = await CreateLeafAsync(null, null);

        // C: fully scheduled (start + end + Space assignment) — IsScheduled, must NOT appear.
        var fullyScheduledId = await CreateLeafAsync(null, null);
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var sched = await _client.PatchAsJsonAsync(
            $"/api/requests/{fullyScheduledId}/schedule",
            new ScheduleRequestRequest { ResourceId = spaceId, StartTs = start, EndTs = end });
        sched.EnsureSuccessStatusCode();

        var partial = await _repo.GetPartiallyScheduledLeavesAsync(includeRequirements: true);

        Assert.Contains(partial, r => r.Id == timedSpacelessId);
        Assert.DoesNotContain(partial, r => r.Id == unscheduledId);
        Assert.DoesNotContain(partial, r => r.Id == fullyScheduledId);

        // Every returned row is a leaf with a start but not fully scheduled (IsScheduled == false).
        Assert.All(partial, r =>
        {
            Assert.Equal(PlanningMode.Leaf, r.PlanningMode);
            Assert.NotNull(r.StartTs);
            Assert.False(r.IsScheduled);
        });
    }
}

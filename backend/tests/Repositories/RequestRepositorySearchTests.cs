using System.Net.Http.Json;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Orkyo.Foundation.Tests.Repositories;

/// <summary>
/// Covers <see cref="IRequestRepository.SearchAsync"/> — the filtered, capped read behind the
/// assistant's <c>get_requests</c> tool. The filters must run in SQL, so these assert on results
/// the old load-everything-then-filter path would also have produced: the point of the query is
/// that it is equivalent, not that it is new behaviour.
///
/// The database is shared with other suites, so every assertion is scoped to a unique name
/// prefix rather than to absolute counts.
/// </summary>
[Collection("Database collection")]
public class RequestRepositorySearchTests
{
    private readonly HttpClient _client;
    private readonly IRequestRepository _repo;

    public RequestRepositorySearchTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        var scope = fixture.Factory.Services.CreateScope();
        _repo = scope.ServiceProvider.GetRequiredService<IRequestRepository>();
    }

    private async Task<Guid> CreateAsync(string name, DateTime? startTs = null, DateTime? endTs = null)
    {
        var resp = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = name,
            StartTs = startTs,
            EndTs = endTs,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
            TargetResourceTypeKeys = [ResourceTypeKeys.Space],
        });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<RequestInfo>())!.Id;
    }

    [Fact]
    public async Task Search_ByName_MatchesSubstringCaseInsensitively_AndExcludesOthers()
    {
        var tag = $"Srch{Guid.NewGuid():N}"[..12];
        var wantedId = await CreateAsync($"{tag}-Wanted");
        var otherId = await CreateAsync($"Other-{Guid.NewGuid():N}"[..24]);

        // Lower-cased on purpose: the filter is ILIKE, not LIKE.
        var found = await _repo.SearchAsync(tag.ToLowerInvariant(), scheduled: null, limit: 50);

        Assert.Contains(found, r => r.Id == wantedId);
        Assert.DoesNotContain(found, r => r.Id == otherId);
    }

    [Fact]
    public async Task Search_HonoursTheRowCap()
    {
        var tag = $"Cap{Guid.NewGuid():N}"[..12];
        await CreateAsync($"{tag}-A");
        await CreateAsync($"{tag}-B");
        await CreateAsync($"{tag}-C");

        var found = await _repo.SearchAsync(tag, scheduled: null, limit: 2);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public async Task Search_TreatsWildcardsInTheQueryAsLiteralText()
    {
        // A caller (or a model) passing "%" must not match every row: the escape in the
        // repository is what keeps this a substring search rather than "select everything".
        var tag = $"Esc{Guid.NewGuid():N}"[..12];
        var plainId = await CreateAsync($"{tag}-plain");
        var literalId = await CreateAsync($"{tag}-100%-done");

        var found = await _repo.SearchAsync($"{tag}-100%-", scheduled: null, limit: 50);

        Assert.Contains(found, r => r.Id == literalId);
        Assert.DoesNotContain(found, r => r.Id == plainId);
    }

    [Fact]
    public async Task Search_ByScheduled_SplitsOnTheSameRuleAsIsScheduled()
    {
        var tag = $"Sched{Guid.NewGuid():N}"[..12];
        var start = DateTime.UtcNow.Date.AddDays(9).AddHours(9);
        var end = start.AddHours(2);

        var unscheduledId = await CreateAsync($"{tag}-none");

        // Timed but with no assignment: IsScheduled is false, because a request that satisfies
        // no target is not scheduled however complete its time window looks.
        var timedOnlyId = await CreateAsync($"{tag}-timed", start, end);

        var fullyScheduledId = await CreateAsync($"{tag}-full");
        var spaceId = await TestHelpers.GetOrCreateTestSpace(_client);
        var sched = await _client.PatchAsJsonAsync(
            $"/api/requests/{fullyScheduledId}/schedule",
            new ScheduleRequestRequest { ResourceId = spaceId, StartTs = start, EndTs = end });
        sched.EnsureSuccessStatusCode();

        var scheduled = await _repo.SearchAsync(tag, scheduled: true, limit: 50);
        var unscheduled = await _repo.SearchAsync(tag, scheduled: false, limit: 50);

        Assert.Contains(scheduled, r => r.Id == fullyScheduledId);
        Assert.DoesNotContain(scheduled, r => r.Id == unscheduledId);
        Assert.DoesNotContain(scheduled, r => r.Id == timedOnlyId);

        Assert.Contains(unscheduled, r => r.Id == unscheduledId);
        Assert.Contains(unscheduled, r => r.Id == timedOnlyId);
        Assert.DoesNotContain(unscheduled, r => r.Id == fullyScheduledId);

        // The SQL predicate and the C# property must agree — that is the whole risk of
        // moving this filter into the database.
        Assert.All(scheduled, r => Assert.True(r.IsScheduled));
        Assert.All(unscheduled, r => Assert.False(r.IsScheduled));
    }

    [Fact]
    public async Task Search_ByLongestDuration_RanksAcrossEveryMatch_NotJustTheFirstPage()
    {
        // The point of sorting in SQL: asking for the top 1 of many must return the real
        // longest, not the longest of whatever page happened to load first.
        var tag = $"Long{Guid.NewGuid():N}"[..12];
        var start = DateTime.UtcNow.Date.AddDays(20).AddHours(8);

        await CreateAsync($"{tag}-short", start, start.AddHours(1));
        var longestId = await CreateAsync($"{tag}-longest", start, start.AddHours(9));
        await CreateAsync($"{tag}-medium", start, start.AddHours(4));

        var top = await _repo.SearchAsync(tag, scheduled: null, limit: 1, RequestSort.LongestDuration);

        Assert.Single(top);
        Assert.Equal(longestId, top[0].Id);
    }

    [Fact]
    public async Task Search_ByLongestDuration_PutsRequestsWithNoWindowLast()
    {
        // A request with no scheduled window has no measurable duration. It must not
        // outrank real windows, and it must not vanish from the results either.
        var tag = $"Null{Guid.NewGuid():N}"[..12];
        var start = DateTime.UtcNow.Date.AddDays(21).AddHours(8);

        var windowlessId = await CreateAsync($"{tag}-none");
        var timedId = await CreateAsync($"{tag}-timed", start, start.AddHours(2));

        var ranked = await _repo.SearchAsync(tag, scheduled: null, limit: 50, RequestSort.LongestDuration);

        var ids = ranked.Select(r => r.Id).ToList();
        Assert.Contains(timedId, ids);
        Assert.Contains(windowlessId, ids);
        Assert.True(ids.IndexOf(timedId) < ids.IndexOf(windowlessId));
    }

    [Fact]
    public async Task Search_ByEarliestStart_OrdersForward()
    {
        var tag = $"Early{Guid.NewGuid():N}"[..12];
        var start = DateTime.UtcNow.Date.AddDays(22).AddHours(8);

        var laterId = await CreateAsync($"{tag}-later", start.AddDays(2), start.AddDays(2).AddHours(1));
        var earlierId = await CreateAsync($"{tag}-earlier", start, start.AddHours(1));

        var ranked = await _repo.SearchAsync(tag, scheduled: null, limit: 50, RequestSort.EarliestStart);

        var ids = ranked.Select(r => r.Id).ToList();
        Assert.True(ids.IndexOf(earlierId) < ids.IndexOf(laterId));
    }

    [Fact]
    public async Task Search_WithNoFilters_ReturnsRowsUpToTheCap()
    {
        await CreateAsync($"NoFilt-{Guid.NewGuid():N}"[..24]);

        var found = await _repo.SearchAsync(nameContains: null, scheduled: null, limit: 5);

        Assert.NotEmpty(found);
        Assert.True(found.Count <= 5);
    }
}

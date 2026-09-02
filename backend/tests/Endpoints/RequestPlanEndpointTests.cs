using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for join conditions end to end: the execution gate that refuses a start
/// while a request's predecessors are unmet, and the planner read model behind the graphical
/// editor. Requests are seeded directly — a dependency is about order, not placement, and these
/// tests set dates explicitly where "done" has to mean something.
/// </summary>
[Collection("Database collection")]
public class RequestPlanEndpointTests
{
    private readonly HttpClient _client;
    private readonly DatabaseFixture _fixture;
    private readonly string _tenantCs;

    private readonly Lazy<Task<Guid>> _site;

    public RequestPlanEndpointTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        _fixture = fixture;
        _tenantCs = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
        _site = new Lazy<Task<Guid>>(SeedSiteAsync);
    }

    /// <summary>
    /// These tests seed requests whose windows have already passed, so they derive to "done".
    /// A site-neutral request is deliberately counted under EVERY site, so leaving them
    /// unscoped would inflate the tenant-wide completion counts other suites assert on. Giving
    /// them a site of their own keeps them out of everyone else's arithmetic.
    /// </summary>
    private async Task<Guid> SeedSiteAsync()
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO sites (id, name, code) VALUES (@id, @name, @code)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Plan Site {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("code", $"PLAN-{id.ToString()[..8]}");
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <param name="finished">Gives the request a window that has already passed, so its
    /// effective status derives to done — which is what the gate counts.</param>
    private async Task<Guid> SeedRequestAsync(
        string planningMode = "leaf",
        Guid? parentId = null,
        bool finished = false,
        string status = "new",
        string predecessorLogic = "all",
        int? k = null,
        string? name = null)
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO requests
                (id, name, status, minimal_duration_value, minimal_duration_unit, planning_mode,
                 parent_request_id, site_id, start_ts, end_ts, predecessor_logic, predecessor_logic_k,
                 created_at, updated_at)
            VALUES
                (@id, @name, @status, 60, 'minutes', @mode, @parent, @site, @start, @end, @logic, @k, NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("site", await _site.Value);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", name ?? $"Plan {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("mode", planningMode);
        cmd.Parameters.AddWithValue("parent", (object?)parentId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("start", finished ? DateTime.UtcNow.AddDays(-3) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("end", finished ? DateTime.UtcNow.AddDays(-1) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("logic", predecessorLogic);
        cmd.Parameters.AddWithValue("k", (object?)k ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task LinkAsync(Guid successor, Guid predecessor) =>
        (await _client.PostAsJsonAsync($"/api/requests/{successor}/dependencies",
            new CreateDependencyRequest { PredecessorRequestId = predecessor }))
        .EnsureSuccessStatusCode();

    private Task<HttpResponseMessage> StartAsync(Guid id) =>
        _client.PutAsJsonAsync($"/api/requests/{id}",
            new UpdateRequestRequest { Status = RequestStatus.InProgress });

    // ── The execution gate ────────────────────────────────────────────────────

    [Fact]
    public async Task StartingWithNoPredecessorsIsAllowed()
    {
        var request = await SeedRequestAsync();

        (await StartAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task StartingIsRefusedWhileAnAllJoinIsUnmet()
    {
        var pending = await SeedRequestAsync(name: "Cut steel");
        var successor = await SeedRequestAsync();
        await LinkAsync(successor, pending);

        var response = await StartAsync(successor);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        // The refusal has to say what is missing and name the offender, or the user is left
        // guessing which of several predecessors is holding it.
        body.Should().Contain("all 1 predecessor must be done");
        body.Should().Contain("Cut steel");
    }

    [Fact]
    public async Task StartingIsAllowedOnceEveryPredecessorIsDone()
    {
        var finished = await SeedRequestAsync(finished: true);
        var successor = await SeedRequestAsync();
        await LinkAsync(successor, finished);

        (await StartAsync(successor)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAnyJoinIsSatisfiedByASinglePredecessor()
    {
        var finished = await SeedRequestAsync(finished: true);
        var pending = await SeedRequestAsync();
        var successor = await SeedRequestAsync(predecessorLogic: "any");
        await LinkAsync(successor, finished);
        await LinkAsync(successor, pending);

        // One of the two is done, which is exactly what "any" asks for.
        (await StartAsync(successor)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AKOfNJoinNeedsK()
    {
        var doneOne = await SeedRequestAsync(finished: true);
        var doneTwo = await SeedRequestAsync(finished: true);
        var pending = await SeedRequestAsync();

        var needsThree = await SeedRequestAsync(predecessorLogic: "k_of_n", k: 3);
        foreach (var pred in new[] { doneOne, doneTwo, pending })
            await LinkAsync(needsThree, pred);

        (await StartAsync(needsThree)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var needsTwo = await SeedRequestAsync(predecessorLogic: "k_of_n", k: 2);
        foreach (var pred in new[] { doneOne, doneTwo, pending })
            await LinkAsync(needsTwo, pred);

        (await StartAsync(needsTwo)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task APredecessorMarkedDoneWithoutDatesSatisfiesTheGate()
    {
        // The deadlock this prevents: effective status derives to "new" for anything unscheduled,
        // so a backlog task marked done counted as unmet and its successor could never start —
        // the only escape being to delete the edge and lose the record that it existed.
        var markedDone = await SeedRequestAsync(status: "done", name: "Permit approved");
        var successor = await SeedRequestAsync();
        await LinkAsync(successor, markedDone);

        (await StartAsync(successor)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlanDoesNotPadlockAChildWhosePredecessorIsMarkedDone()
    {
        // Same rule, same reading — the planner and the gate must not disagree about who is
        // blocked, or the padlock becomes something users learn to ignore.
        var parent = await SeedRequestAsync(planningMode: "summary");
        var markedDone = await SeedRequestAsync(parentId: parent, status: "done");
        var child = await SeedRequestAsync(parentId: parent);
        await LinkAsync(child, markedDone);

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        plan!.Children.Single(c => c.Id == child).CanStart.Should().BeTrue();
    }

    [Fact]
    public async Task PlanDoesNotPadlockAChildThatIsAlreadyUnderWay()
    {
        // The gate exempts an already-started request, so the planner must too.
        var parent = await SeedRequestAsync(planningMode: "summary");
        var pending = await SeedRequestAsync(parentId: parent);
        var started = await SeedRequestAsync(parentId: parent, status: "in_progress");
        await LinkAsync(started, pending);

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        plan!.Children.Single(c => c.Id == started).CanStart.Should().BeTrue();
    }

    [Fact]
    public async Task ACancelledPredecessorDoesNotHoldTheGateShut()
    {
        // Without the exclusion this request could never start: the only way out would be to
        // delete the edge and lose the record that the work was ever planned.
        var cancelled = await SeedRequestAsync(status: "cancelled");
        var successor = await SeedRequestAsync();
        await LinkAsync(successor, cancelled);

        (await StartAsync(successor)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnAlreadyStartedRequestCanStillBeEdited()
    {
        // The gate guards the transition into in_progress, not every later save. Re-sending the
        // status while renaming must not be refused by a condition already accepted.
        var pending = await SeedRequestAsync();
        var successor = await SeedRequestAsync(status: "in_progress");
        await LinkAsync(successor, pending);

        var response = await _client.PutAsJsonAsync($"/api/requests/{successor}",
            new UpdateRequestRequest { Name = "Renamed", Status = RequestStatus.InProgress });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AViewerMayReadThePlanButNotStartWork()
    {
        // The plan is on the member-read half of the group, like every other request read; the
        // status change is a write. Asserting both keeps a later refactor from silently moving
        // the endpoint to the wrong side of that line.
        var parent = await SeedRequestAsync(planningMode: "summary");
        await SeedRequestAsync(parentId: parent);

        using var viewer = _fixture.CreateClientWithRole("viewer");

        (await viewer.GetAsync($"/api/requests/{parent}/plan"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var write = await viewer.PutAsJsonAsync($"/api/requests/{parent}",
            new UpdateRequestRequest { Status = RequestStatus.InProgress });
        write.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    // ── The planner read model ────────────────────────────────────────────────

    [Fact]
    public async Task PlanReturnsChildrenEdgesAndStartability()
    {
        var parent = await SeedRequestAsync(planningMode: "summary");
        var first = await SeedRequestAsync(parentId: parent, finished: true, name: "First");
        var second = await SeedRequestAsync(parentId: parent, name: "Second");
        var third = await SeedRequestAsync(parentId: parent, name: "Third");
        await LinkAsync(second, first);
        await LinkAsync(third, second);

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        plan!.ParentId.Should().Be(parent);
        plan.Children.Should().HaveCount(3);
        plan.Edges.Should().HaveCount(2);

        // First has nothing to wait for; second's predecessor is done; third's is not.
        plan.Children.Single(c => c.Id == first).CanStart.Should().BeTrue();
        plan.Children.Single(c => c.Id == second).CanStart.Should().BeTrue();
        plan.Children.Single(c => c.Id == third).CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task PlanCountsEdgesThatLeaveTheGroupWithoutDrawingThem()
    {
        var parent = await SeedRequestAsync(planningMode: "summary");
        var child = await SeedRequestAsync(parentId: parent);
        var outsider = await SeedRequestAsync();
        var downstream = await SeedRequestAsync();

        await LinkAsync(child, outsider);       // into the group
        await LinkAsync(downstream, child);     // out of the group

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        // Neither edge has a second node inside the group, so neither is drawable — but the
        // planner still has to say the task is entangled outside it.
        plan!.Edges.Should().BeEmpty();
        var only = plan.Children.Single();
        only.ExternalPredecessorCount.Should().Be(1);
        only.ExternalSuccessorCount.Should().Be(1);
    }

    [Fact]
    public async Task PlanCountsAPredecessorOutsideTheGroupAgainstStartability()
    {
        // A task waiting on work in another group is no more startable for the planner being
        // unable to draw the edge.
        var parent = await SeedRequestAsync(planningMode: "summary");
        var child = await SeedRequestAsync(parentId: parent);
        var outsider = await SeedRequestAsync();
        await LinkAsync(child, outsider);

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        plan!.Children.Single().CanStart.Should().BeFalse();
    }

    [Fact]
    public async Task PlanReportsTheJoinConditionOfEachChild()
    {
        var parent = await SeedRequestAsync(planningMode: "summary");
        var child = await SeedRequestAsync(parentId: parent, predecessorLogic: "k_of_n", k: 2);

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{parent}/plan");

        var only = plan!.Children.Single();
        only.PredecessorLogic.Should().Be(PredecessorLogic.KOfN);
        only.PredecessorLogicK.Should().Be(2);
    }

    [Fact]
    public async Task PlanForARequestWithoutChildrenIsEmptyRatherThanMissing()
    {
        var lonely = await SeedRequestAsync();

        var plan = await _client.GetFromJsonAsync<RequestPlan>($"/api/requests/{lonely}/plan");

        plan!.Children.Should().BeEmpty();
        plan.Edges.Should().BeEmpty();
    }

    [Fact]
    public async Task PlanForAnUnknownRequestIs404()
    {
        var response = await _client.GetAsync($"/api/requests/{Guid.NewGuid()}/plan");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

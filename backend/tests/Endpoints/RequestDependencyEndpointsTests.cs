using System.Net;
using System.Net.Http.Json;
using Api.Models;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for the precedence-edge endpoints. These carry the parts the unit tests
/// cannot: the recursive cycle walk, the unique-edge constraint, and the FK cascade that
/// removes edges with their endpoints. Requests are seeded directly so no scheduling is
/// implied — an edge is about order, not placement.
/// </summary>
[Collection("Database collection")]
public class RequestDependencyEndpointsTests
{
    private readonly HttpClient _client;
    private readonly string _tenantCs;

    public RequestDependencyEndpointsTests(DatabaseFixture fixture)
    {
        _client = fixture.CreateAuthorizedClient();
        _tenantCs = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    private async Task<Guid> SeedRequestAsync(string planningMode = "leaf")
    {
        var id = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO requests
                (id, name, status, minimal_duration_value, minimal_duration_unit, planning_mode, created_at, updated_at)
            VALUES
                (@id, @name, 'new', 60, 'minutes', @mode, NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("name", $"Dep {id.ToString()[..8]}");
        cmd.Parameters.AddWithValue("mode", planningMode);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<HttpResponseMessage> LinkAsync(Guid successor, Guid predecessor, int lagMinutes = 0) =>
        await _client.PostAsJsonAsync($"/api/requests/{successor}/dependencies",
            new CreateDependencyRequest { PredecessorRequestId = predecessor, LagMinutes = lagMinutes });

    [Fact]
    public async Task Create_ThenRead_ReturnsEdgeFromBothEnds()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();

        var response = await LinkAsync(succ, pred, lagMinutes: 120);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<RequestDependencyInfo>();
        created!.LagMinutes.Should().Be(120);
        created.DependencyType.Should().Be(DependencyTypes.FinishToStart);

        // The successor sees it as a predecessor…
        var forSuccessor = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{succ}/dependencies");
        forSuccessor!.Predecessors.Should().ContainSingle(e => e.PredecessorRequestId == pred);
        forSuccessor.Successors.Should().BeEmpty();

        // …and the predecessor sees the same edge as a successor.
        var forPredecessor = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{pred}/dependencies");
        forPredecessor!.Successors.Should().ContainSingle(e => e.SuccessorRequestId == succ);
        forPredecessor.Predecessors.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_Duplicate_Returns409()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();

        (await LinkAsync(succ, pred)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LinkAsync(succ, pred)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_SelfEdge_Returns409()
    {
        var request = await SeedRequestAsync();
        (await LinkAsync(request, request)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_GroupEndpoint_Returns409()
    {
        var group = await SeedRequestAsync("summary");
        var leaf = await SeedRequestAsync();

        (await LinkAsync(leaf, group)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LinkAsync(group, leaf)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_UnknownPredecessor_Returns404()
    {
        var succ = await SeedRequestAsync();
        (await LinkAsync(succ, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_DirectCycle_Returns409()
    {
        var a = await SeedRequestAsync();
        var b = await SeedRequestAsync();

        (await LinkAsync(b, a)).StatusCode.Should().Be(HttpStatusCode.Created);
        // b already waits for a, so a waiting for b closes the loop.
        (await LinkAsync(a, b)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_TransitiveCycle_Returns409()
    {
        var a = await SeedRequestAsync();
        var b = await SeedRequestAsync();
        var c = await SeedRequestAsync();

        (await LinkAsync(b, a)).StatusCode.Should().Be(HttpStatusCode.Created);  // b waits for a
        (await LinkAsync(c, b)).StatusCode.Should().Be(HttpStatusCode.Created);  // c waits for b

        // a waiting for c would close a → b → c → a. The walk has to follow more than one hop.
        (await LinkAsync(a, c)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_DiamondIsNotACycle()
    {
        var start = await SeedRequestAsync();
        var left = await SeedRequestAsync();
        var right = await SeedRequestAsync();
        var join = await SeedRequestAsync();

        (await LinkAsync(left, start)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LinkAsync(right, start)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LinkAsync(join, left)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Converging paths are legitimate; only a back-edge is a cycle.
        (await LinkAsync(join, right)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Delete_RemovesEdge()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();

        var created = await (await LinkAsync(succ, pred)).Content.ReadFromJsonAsync<RequestDependencyInfo>();

        var deleted = await _client.DeleteAsync($"/api/requests/{succ}/dependencies/{created!.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{succ}/dependencies");
        after!.Predecessors.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ThroughUnrelatedRequest_Returns404()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();
        var bystander = await SeedRequestAsync();

        var created = await (await LinkAsync(succ, pred)).Content.ReadFromJsonAsync<RequestDependencyInfo>();

        var response = await _client.DeleteAsync($"/api/requests/{bystander}/dependencies/{created!.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingARequest_CascadesItsEdges()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();
        (await LinkAsync(succ, pred)).StatusCode.Should().Be(HttpStatusCode.Created);

        await using (var conn = new NpgsqlConnection(_tenantCs))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("DELETE FROM requests WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", pred);
            await cmd.ExecuteNonQueryAsync();
        }

        // The edge goes with its endpoint — a dangling edge has no meaning.
        var after = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{succ}/dependencies");
        after!.Predecessors.Should().BeEmpty();
    }

    [Fact]
    public async Task TurningALinkedLeafIntoAGroup_Returns409()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();
        (await LinkAsync(succ, pred)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await _client.PutAsJsonAsync($"/api/requests/{succ}",
            new UpdateRequestRequest { PlanningMode = PlanningMode.Summary });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "response was {0}", body);
    }

    [Fact]
    public async Task GetAll_ListsSeededEdge()
    {
        var pred = await SeedRequestAsync();
        var succ = await SeedRequestAsync();
        (await LinkAsync(succ, pred)).StatusCode.Should().Be(HttpStatusCode.Created);

        var all = await _client.GetFromJsonAsync<List<RequestDependencyInfo>>("/api/requests/dependencies");
        all.Should().Contain(e => e.PredecessorRequestId == pred && e.SuccessorRequestId == succ);
    }
}

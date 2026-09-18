using System.Net;
using System.Net.Http.Json;
using Api.Constants;
using Api.Models;
using Npgsql;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Integration tests for routings. The unit tests cover what the service builds; these cover
/// what only the database can show: the step rows and their RESTRICT on templates, the atomic
/// chain (a refused instantiation leaves no rows), and the leaves' requirements and edges as
/// the request endpoints read them back.
/// </summary>
[Collection("Database collection")]
public class RoutingEndpointsTests
{
    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _client;
    private readonly string _tenantCs;

    public RoutingEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.CreateAuthorizedClient();
        _tenantCs = $"Host=localhost;Port={fixture.DatabasePort};Database={TestConstants.TenantDatabase};Username=postgres;Password=postgres";
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}"[..30];

    /// <summary>A request template that targets the tool type — an operation a routing can use.</summary>
    private async Task<Template> CreateOperationAsync(string name, IReadOnlyList<string>? targets = null, string entityType = "request")
    {
        var response = await _client.PostAsJsonAsync("/api/templates", new CreateTemplateRequest
        {
            Name = Unique(name),
            EntityType = entityType,
            DurationValue = entityType == "request" ? 1 : null,
            DurationUnit = entityType == "request" ? "hours" : null,
            TargetResourceTypeKeys = entityType == "request" ? targets ?? [ResourceTypeKeys.Tool] : null,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Template>())!;
    }

    private async Task<Guid> CreateCriterionAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = Unique("Material"),
            DataType = CriterionDataType.String,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CriterionInfo>())!.Id;
    }

    private static RoutingStepRequest Step(int no, Guid template, int setup = 0, int run = 0, int lag = 0) =>
        new() { StepNo = no, OperationTemplateId = template, SetupMinutes = setup, RunMinutesPerUnit = run, LagMinutesAfter = lag };

    private async Task<RoutingInfo> CreateRoutingAsync(params RoutingStepRequest[] steps)
    {
        var response = await _client.PostAsJsonAsync("/api/routings", new CreateRoutingRequest
        {
            Name = Unique("Bracket"),
            Steps = steps,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<RoutingInfo>())!;
    }

    private async Task<int> CountRequestsNamedAsync(string name)
    {
        await using var conn = new NpgsqlConnection(_tenantCs);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM requests WHERE name = @name OR description = @name", conn);
        cmd.Parameters.AddWithValue("name", name);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── Templates carry target types ─────────────────────────────────────────────

    [Fact]
    public async Task Template_RoundTripsTargetResourceTypes()
    {
        var created = await CreateOperationAsync("Mill", [ResourceTypeKeys.Tool]);
        created.TargetResourceTypeKeys.Should().Equal(ResourceTypeKeys.Tool);

        var read = await _client.GetFromJsonAsync<Template>($"/api/templates/{created.Id}");
        read!.TargetResourceTypeKeys.Should().Equal(ResourceTypeKeys.Tool);

        // Null on update leaves them; an empty list clears them.
        var kept = await _client.PutAsJsonAsync($"/api/templates/{created.Id}", new UpdateTemplateRequest
        {
            Name = created.Name,
            EntityType = "request",
            DurationValue = 2,
            DurationUnit = "hours",
        });
        (await kept.Content.ReadFromJsonAsync<Template>())!.TargetResourceTypeKeys.Should().Equal(ResourceTypeKeys.Tool);

        var cleared = await _client.PutAsJsonAsync($"/api/templates/{created.Id}", new UpdateTemplateRequest
        {
            Name = created.Name,
            EntityType = "request",
            DurationValue = 2,
            DurationUnit = "hours",
            TargetResourceTypeKeys = [],
        });
        (await cleared.Content.ReadFromJsonAsync<Template>())!.TargetResourceTypeKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task Template_WithUnknownTargetType_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/templates", new CreateTemplateRequest
        {
            Name = Unique("Ghost"),
            EntityType = "request",
            DurationValue = 1,
            DurationUnit = "hours",
            TargetResourceTypeKeys = ["no-such-type"],
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Routing CRUD ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ThenRead_ReturnsStepsInOrderWithOperationNames()
    {
        var saw = await CreateOperationAsync("Saw");
        var mill = await CreateOperationAsync("Mill");

        // Steps arrive out of order on purpose; the read returns them by step number.
        var created = await CreateRoutingAsync(Step(2, mill.Id, setup: 30, run: 60, lag: 240), Step(1, saw.Id, setup: 10, run: 5));

        created.Steps.Select(s => s.StepNo).Should().Equal(1, 2);
        created.Steps.Select(s => s.OperationName).Should().Equal(saw.Name, mill.Name);
        created.Steps[1].LagMinutesAfter.Should().Be(240);

        var read = await _client.GetFromJsonAsync<RoutingInfo>($"/api/routings/{created.Id}");
        read!.Steps.Should().HaveCount(2);

        var all = await _client.GetFromJsonAsync<List<RoutingInfo>>("/api/routings");
        all!.Should().Contain(r => r.Id == created.Id);
    }

    [Fact]
    public async Task Update_ReplacesTheStepsWholesale()
    {
        var saw = await CreateOperationAsync("Saw");
        var mill = await CreateOperationAsync("Mill");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5), Step(2, mill.Id, run: 60));

        var response = await _client.PutAsJsonAsync($"/api/routings/{routing.Id}", new UpdateRoutingRequest
        {
            Name = "Bracket v2",
            Steps = [Step(1, mill.Id, run: 45)],
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<RoutingInfo>();
        updated!.Name.Should().Be("Bracket v2");
        updated.Steps.Should().ContainSingle().Which.OperationTemplateId.Should().Be(mill.Id);
    }

    [Fact]
    public async Task Create_WithGapInStepNumbers_Returns400()
    {
        var saw = await CreateOperationAsync("Saw");
        var response = await _client.PostAsJsonAsync("/api/routings", new CreateRoutingRequest
        {
            Name = Unique("Gappy"),
            Steps = [Step(1, saw.Id, run: 5), Step(3, saw.Id, run: 5)],
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_WithSpaceTemplateAsOperation_Returns409()
    {
        var room = await CreateOperationAsync("Room", targets: null, entityType: "space");
        var response = await _client.PostAsJsonAsync("/api/routings", new CreateRoutingRequest
        {
            Name = Unique("Wrong"),
            Steps = [Step(1, room.Id, run: 5)],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_WithUnknownTemplate_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/api/routings", new CreateRoutingRequest
        {
            Name = Unique("Ghost"),
            Steps = [Step(1, Guid.NewGuid(), run: 5)],
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Routing_Returns204ThenNotFound()
    {
        var saw = await CreateOperationAsync("Saw");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5));

        (await _client.DeleteAsync($"/api/routings/{routing.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/api/routings/{routing.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/routings/{routing.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_TemplateUsedAsOperation_Returns409UntilTheRoutingGoes()
    {
        var saw = await CreateOperationAsync("Saw");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5));

        (await _client.DeleteAsync($"/api/templates/{saw.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await _client.DeleteAsync($"/api/routings/{routing.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/api/templates/{saw.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Viewer_CannotWriteRoutings()
    {
        var saw = await CreateOperationAsync("Saw");
        var viewer = _fixture.CreateClientWithRole("viewer");

        var response = await viewer.PostAsJsonAsync("/api/routings", new CreateRoutingRequest
        {
            Name = Unique("Viewer"),
            Steps = [Step(1, saw.Id, run: 5)],
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await viewer.GetAsync("/api/routings")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Instantiation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Instantiate_BuildsTheContainerItsLeavesAndTheChain()
    {
        var criterionId = await CreateCriterionAsync();
        var saw = await CreateOperationAsync("Saw");
        var mill = await CreateOperationAsync("Mill");
        var deburr = await CreateOperationAsync("Deburr");

        // Mill carries a requirement; every mill leaf must inherit it.
        var item = await _client.PostAsJsonAsync($"/api/templates/{mill.Id}/items",
            new CreateTemplateItemRequest { CriterionId = criterionId, Value = "\"steel\"" });
        item.StatusCode.Should().Be(HttpStatusCode.Created);

        var routing = await CreateRoutingAsync(
            Step(1, saw.Id, setup: 10, run: 5),
            Step(2, mill.Id, setup: 30, run: 60, lag: 240),
            Step(3, deburr.Id, run: 15));

        var earliest = new DateTime(2026, 3, 2, 8, 0, 0, DateTimeKind.Utc);
        var latest = new DateTime(2026, 3, 6, 17, 0, 0, DateTimeKind.Utc);
        var name = Unique("WO");

        var response = await _client.PostAsJsonAsync($"/api/routings/{routing.Id}/instantiate", new InstantiateRoutingRequest
        {
            Name = name,
            Quantity = 4,
            EarliestStartTs = earliest,
            LatestEndTs = latest,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<InstantiateRoutingResponse>();
        response.Headers.Location!.ToString().Should().EndWith($"/api/requests/{result!.Parent.Id}");
        result.Parent.Name.Should().Be(name);
        result.Parent.PlanningMode.Should().Be(PlanningMode.Container);
        result.Parent.Description.Should().Be(routing.Name);
        result.Parent.EarliestStartTs.Should().Be(earliest);
        result.Parent.LatestEndTs.Should().Be(latest);
        result.Parent.MinimalDurationUnit.Should().Be(DurationUnit.Minutes);
        result.Parent.MinimalDurationValue.Should().Be(30 + 270 + 60);
        result.ChildIds.Should().HaveCount(3);

        var leaves = new List<RequestInfo>();
        foreach (var id in result.ChildIds)
            leaves.Add((await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{id}"))!);

        // setup + run × 4, in minutes, one leaf per step, under the container.
        leaves.Select(l => l.MinimalDurationValue).Should().Equal(30, 270, 60);
        leaves.Should().OnlyContain(l => l.MinimalDurationUnit == DurationUnit.Minutes);
        leaves.Should().OnlyContain(l => l.PlanningMode == PlanningMode.Leaf);
        leaves.Should().OnlyContain(l => l.ParentRequestId == result.Parent.Id);
        leaves.Select(l => l.SortOrder).Should().Equal(1, 2, 3);
        leaves.Select(l => l.Name).Should().Equal($"1. {saw.Name}", $"2. {mill.Name}", $"3. {deburr.Name}");
        leaves.Should().OnlyContain(l => l.EarliestStartTs == earliest && l.LatestEndTs == latest);
        leaves.Should().OnlyContain(l => l.TargetResourceTypeKeys.SequenceEqual(new[] { ResourceTypeKeys.Tool }));

        var millLeaf = leaves[1];
        millLeaf.Requirements.Should().ContainSingle(r => r.CriterionId == criterionId)
            .Which.Value.GetString().Should().Be("steel");
        leaves[0].Requirements.Should().BeNullOrEmpty();

        // Finish-to-start chain with the step's lag on the edge that leaves it.
        var first = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{leaves[0].Id}/dependencies");
        first!.Predecessors.Should().BeEmpty();
        first.Successors.Should().ContainSingle(e => e.SuccessorRequestId == leaves[1].Id && e.LagMinutes == 0);

        var second = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{leaves[1].Id}/dependencies");
        second!.Predecessors.Should().ContainSingle(e => e.PredecessorRequestId == leaves[0].Id);
        second.Successors.Should().ContainSingle(e => e.SuccessorRequestId == leaves[2].Id && e.LagMinutes == 240);

        var third = await _client.GetFromJsonAsync<RequestDependencies>($"/api/requests/{leaves[2].Id}/dependencies");
        third!.Predecessors.Should().ContainSingle(e => e.PredecessorRequestId == leaves[1].Id && e.LagMinutes == 240);
        third.Successors.Should().BeEmpty();
    }

    [Fact]
    public async Task Instantiate_UnderAContainer_NestsTheWorkOrder()
    {
        var saw = await CreateOperationAsync("Saw");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5));

        var lineResponse = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = Unique("Line"),
            PlanningMode = PlanningMode.Container,
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
        });
        lineResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var line = await lineResponse.Content.ReadFromJsonAsync<RequestInfo>();

        var response = await _client.PostAsJsonAsync($"/api/routings/{routing.Id}/instantiate", new InstantiateRoutingRequest
        {
            Name = Unique("WO"),
            Quantity = 1,
            ParentRequestId = line!.Id,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<InstantiateRoutingResponse>())!.Parent.ParentRequestId.Should().Be(line.Id);
    }

    [Fact]
    public async Task Instantiate_UnderALeaf_Returns409()
    {
        var saw = await CreateOperationAsync("Saw");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5));

        var leafResponse = await _client.PostAsJsonAsync("/api/requests", new CreateRequestRequest
        {
            Name = Unique("Leaf"),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Hours,
        });
        var leaf = await leafResponse.Content.ReadFromJsonAsync<RequestInfo>();

        var response = await _client.PostAsJsonAsync($"/api/routings/{routing.Id}/instantiate", new InstantiateRoutingRequest
        {
            Name = Unique("WO"),
            Quantity = 1,
            ParentRequestId = leaf!.Id,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Instantiate_OperationWithoutTargetType_Returns409AndWritesNothing()
    {
        var saw = await CreateOperationAsync("Saw");
        var untargeted = await CreateOperationAsync("Inspect", targets: []);
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5), Step(2, untargeted.Id, run: 5));
        var name = Unique("WO");

        var response = await _client.PostAsJsonAsync($"/api/routings/{routing.Id}/instantiate", new InstantiateRoutingRequest
        {
            Name = name,
            Quantity = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Step 2");
        (await CountRequestsNamedAsync(name)).Should().Be(0, "the refusal comes before the transaction opens");
    }

    [Fact]
    public async Task Instantiate_UnknownRouting_Returns404()
    {
        var response = await _client.PostAsJsonAsync($"/api/routings/{Guid.NewGuid()}/instantiate", new InstantiateRoutingRequest
        {
            Name = Unique("WO"),
            Quantity = 1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Instantiate_WithZeroQuantity_Returns400()
    {
        var saw = await CreateOperationAsync("Saw");
        var routing = await CreateRoutingAsync(Step(1, saw.Id, run: 5));

        var response = await _client.PostAsJsonAsync($"/api/routings/{routing.Id}/instantiate", new InstantiateRoutingRequest
        {
            Name = Unique("WO"),
            Quantity = 0,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

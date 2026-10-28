using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Api.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Optimistic concurrency (#299): a write that carries <c>If-Match: "&lt;version&gt;"</c> fails
/// with 412 when the row changed since the caller read it, and a write without the header keeps
/// last-write-wins. Also the race the same change closes: two writers on one version.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class OptimisticConcurrencyTests
{
    private readonly HttpClient _client;
    private readonly ISchedulingRepository _scheduling;

    public OptimisticConcurrencyTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
        _scheduling = databaseFixture.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<ISchedulingRepository>();
    }

    #region Requests

    [Fact]
    public async Task UpdateRequest_WithCurrentVersion_SucceedsAndChangesVersion()
    {
        var created = await CreateRequestAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "Renamed" }, created.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.Equal("Renamed", updated!.Name);
        Assert.NotEqual(created.Version, updated.Version);
    }

    [Fact]
    public async Task UpdateRequest_WithStaleVersion_Returns412AndKeepsTheOtherWrite()
    {
        var created = await CreateRequestAsync();
        await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "First writer" }, created.Version);

        var response = await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "Second writer" }, created.Version);

        await AssertPreconditionFailedAsync(response);
        var current = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{created.Id}");
        Assert.Equal("First writer", current!.Name);
    }

    [Fact]
    public async Task UpdateRequest_WithoutIfMatch_LastWriteWins()
    {
        var created = await CreateRequestAsync();
        await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "First writer" }, created.Version);

        var response = await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "Second writer" }, version: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRequest_WithMalformedIfMatch_Returns400()
    {
        // IfMatchTests covers the parser; this proves its refusal reaches the client as a 400.
        var created = await CreateRequestAsync();
        using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/requests/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateRequestRequest { Name = "Renamed" }),
        };
        message.Headers.TryAddWithoutValidation("If-Match", "W/\"1\"");

        var response = await _client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Writes_UnknownIdWithIfMatch_Return404Not412()
    {
        var id = Guid.NewGuid();
        var start = DateTime.UtcNow.Date.AddDays(10);

        var responses = new[]
        {
            await SendAsync(HttpMethod.Put, $"/api/requests/{id}", new UpdateRequestRequest { Name = "Renamed" }, version: 1),
            await SendAsync(HttpMethod.Patch, $"/api/requests/{id}/schedule",
                new ScheduleRequestRequest { ResourceId = DatabaseFixture.SpaceId, StartTs = start, EndTs = start.AddHours(2) }, version: 1),
            await SendAsync(HttpMethod.Patch, $"/api/requests/{id}/move",
                new MoveRequestRequest { NewParentRequestId = null, SortOrder = 1 }, version: 1),
            await SendAsync(HttpMethod.Put, $"/api/resources/{id}", new UpdateResourceRequest { Name = "Renamed" }, version: 1),
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    [Fact]
    public async Task AddAndDeleteRequirement_EachChangeTheVersion()
    {
        // Its own criterion: a shared one may have been narrowed by another test.
        var criterion = await (await _client.PostAsJsonAsync("/api/criteria", new CreateCriterionRequest
        {
            Name = $"concurrency_{Guid.NewGuid():N}",
            DataType = CriterionDataType.Boolean,
            ResourceTypeKeys = [ResourceTypeKeys.Space],
        })).Content.ReadFromJsonAsync<CriterionInfo>();
        var created = await CreateRequestAsync(new CreateRequestRequest
        {
            Name = $"Reqs-{Guid.NewGuid():N}"[..20],
            ResourceIds = [DatabaseFixture.SpaceId],
            StartTs = DateTime.UtcNow.AddDays(1),
            EndTs = DateTime.UtcNow.AddDays(2),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
        });

        var add = await _client.PostAsJsonAsync($"/api/requests/{created.Id}/requirements",
            new CreateRequestRequirementRequest { CriterionId = criterion!.Id, Value = JsonSerializer.SerializeToElement(true) });
        Assert.True(add.StatusCode == HttpStatusCode.Created, await add.Content.ReadAsStringAsync());
        var requirement = await add.Content.ReadFromJsonAsync<RequestRequirementInfo>();
        var afterAdd = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{created.Id}");
        Assert.NotEqual(created.Version, afterAdd!.Version);

        await _client.DeleteAsync($"/api/requests/{created.Id}/requirements/{requirement!.Id}");
        var afterDelete = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{created.Id}");
        Assert.NotEqual(afterAdd.Version, afterDelete!.Version);
    }

    [Fact]
    public async Task UpdateRequest_ChangingOnlyTargets_ChangesVersion()
    {
        // Targets live in a child table; the request row must still move, or a second editor
        // could overwrite this change unseen.
        var created = await CreateRequestAsync();

        var response = await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { TargetResourceTypeKeys = [] }, created.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<RequestInfo>();
        Assert.NotEqual(created.Version, updated!.Version);
    }

    [Fact]
    public async Task ScheduleRequest_WithStaleVersion_Returns412()
    {
        var created = await CreateRequestAsync();
        await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "Moved on" }, created.Version);

        var start = DateTime.UtcNow.Date.AddDays(10);
        var response = await SendAsync(HttpMethod.Patch, $"/api/requests/{created.Id}/schedule",
            new ScheduleRequestRequest { ResourceId = DatabaseFixture.SpaceId, StartTs = start, EndTs = start.AddHours(2) },
            created.Version);

        await AssertPreconditionFailedAsync(response);
    }

    [Fact]
    public async Task MoveRequest_WithStaleVersion_Returns412()
    {
        var created = await CreateRequestAsync();
        await SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}",
            new UpdateRequestRequest { Name = "Moved on" }, created.Version);

        var response = await SendAsync(HttpMethod.Patch, $"/api/requests/{created.Id}/move",
            new MoveRequestRequest { NewParentRequestId = null, SortOrder = 1 }, created.Version);

        await AssertPreconditionFailedAsync(response);
    }

    [Fact]
    public async Task UpdateRequest_TwoWritersOnOneVersion_ExactlyOneWins()
    {
        // A smoke test: the two calls usually run one after the other. When they do overlap, the
        // FOR UPDATE lock makes the second wait and then see the first one's version.
        var created = await CreateRequestAsync();

        var responses = await Task.WhenAll(
            SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}", new UpdateRequestRequest { Name = "A" }, created.Version),
            SendAsync(HttpMethod.Put, $"/api/requests/{created.Id}", new UpdateRequestRequest { Name = "B" }, created.Version));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.PreconditionFailed);
    }

    #endregion

    #region Resources

    [Fact]
    public async Task UpdateResource_WithCurrentVersion_SucceedsAndChangesVersion()
    {
        var created = await TestHelpers.CreatePersonAsync(_client);

        var response = await SendAsync(HttpMethod.Put, $"/api/resources/{created.Id}",
            new UpdateResourceRequest { Name = "Renamed person" }, created.Version);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ResourceInfo>();
        Assert.NotEqual(created.Version, updated!.Version);
    }

    [Fact]
    public async Task UpdateResource_WithStaleVersion_Returns412()
    {
        var created = await TestHelpers.CreatePersonAsync(_client);
        await SendAsync(HttpMethod.Put, $"/api/resources/{created.Id}",
            new UpdateResourceRequest { Name = "First writer" }, created.Version);

        var response = await SendAsync(HttpMethod.Put, $"/api/resources/{created.Id}",
            new UpdateResourceRequest { Name = "Second writer" }, created.Version);

        await AssertPreconditionFailedAsync(response);
    }

    [Fact]
    public async Task UpdateResource_WithNoChangedFieldsAndStaleVersion_Returns412()
    {
        var created = await TestHelpers.CreatePersonAsync(_client);
        await SendAsync(HttpMethod.Put, $"/api/resources/{created.Id}",
            new UpdateResourceRequest { Name = "First writer" }, created.Version);

        var response = await SendAsync(HttpMethod.Put, $"/api/resources/{created.Id}",
            new UpdateResourceRequest(), created.Version);

        await AssertPreconditionFailedAsync(response);
    }

    #endregion

    #region Assignments

    [Fact]
    public async Task CreateAssignment_ChangesTheRequestVersion()
    {
        var tool = await CreateExclusiveToolAsync();
        var request = await CreateRequestAsync();

        var create = await _client.PostAsJsonAsync("/api/resource-assignments", Booking(tool.Id, request.Id));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var after = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{request.Id}");
        Assert.NotEqual(request.Version, after!.Version);
    }

    [Fact]
    public async Task CancelAssignment_ChangesTheRequestVersion()
    {
        var tool = await CreateExclusiveToolAsync();
        var request = await CreateRequestAsync();
        var assignment = await (await _client.PostAsJsonAsync("/api/resource-assignments", Booking(tool.Id, request.Id)))
            .Content.ReadFromJsonAsync<ResourceAssignmentInfo>();
        var before = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{request.Id}");

        var cancel = await _client.DeleteAsync($"/api/resource-assignments/{assignment!.Id}");

        Assert.True(cancel.IsSuccessStatusCode);
        var after = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{request.Id}");
        Assert.NotEqual(before!.Version, after!.Version);
    }

    #endregion

    #region Scheduling settings

    [Fact]
    public async Task SaveSchedulingSettings_VersionZeroCreatesOnlyOnce()
    {
        var siteId = await CreateSiteAsync();
        var defaults = await _client.GetFromJsonAsync<SchedulingSettingsInfo>($"/api/sites/{siteId}/scheduling");
        Assert.Equal(0u, defaults!.Version);

        var create = await SendAsync(HttpMethod.Put, $"/api/sites/{siteId}/scheduling", Settings("Europe/Berlin"), version: 0);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var stored = await create.Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        Assert.NotEqual(0u, stored!.Version);

        // A second editor who also read the defaults must not overwrite the first one's row.
        var second = await SendAsync(HttpMethod.Put, $"/api/sites/{siteId}/scheduling", Settings("Europe/Paris"), version: 0);
        await AssertPreconditionFailedAsync(second);

        var update = await SendAsync(HttpMethod.Put, $"/api/sites/{siteId}/scheduling", Settings("Europe/Paris"), stored.Version);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task SaveSchedulingSettings_AfterAReset_StaleVersionReturns412()
    {
        // The settings were reset to defaults after the caller read them: a save must not
        // silently re-create what the other user removed.
        var siteId = await CreateSiteAsync();
        var stored = await (await SendAsync(HttpMethod.Put, $"/api/sites/{siteId}/scheduling", Settings("UTC"), version: 0))
            .Content.ReadFromJsonAsync<SchedulingSettingsInfo>();
        await _client.DeleteAsync($"/api/sites/{siteId}/scheduling");

        var response = await SendAsync(HttpMethod.Put, $"/api/sites/{siteId}/scheduling", Settings("UTC"), stored!.Version);

        await AssertPreconditionFailedAsync(response);
    }

    [Fact]
    public async Task SaveSchedulingSettings_LeavesTheVersionOfAnUnmovedRequest()
    {
        // Round-the-clock settings recalculate a 1-day request to the window it already has, so
        // its row must not be rewritten: an editor holding it open would be refused for nothing.
        // Its own site and space: settings on the shared fixture site would leak into other tests.
        var siteId = await CreateSiteAsync();
        var spaceId = await TestHelpers.CreateUniqueTestSpace(_client, siteId);
        var start = DateTime.UtcNow.Date.AddDays(30);
        var scheduled = await CreateRequestAsync(new CreateRequestRequest
        {
            Name = $"Unmoved-{Guid.NewGuid():N}"[..20],
            ResourceIds = [spaceId],
            StartTs = start,
            EndTs = start.AddDays(1),
            MinimalDurationValue = 1,
            MinimalDurationUnit = DurationUnit.Days,
            SchedulingSettingsApply = true,
        });

        var save = await _client.PutAsJsonAsync($"/api/sites/{siteId}/scheduling", new UpsertSchedulingSettingsRequest
        {
            TimeZone = "UTC",
            WorkingHoursEnabled = false,
            WeekendsEnabled = true,
            PublicHolidaysEnabled = false,
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var after = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{scheduled.Id}");
        Assert.Equal(scheduled.Version, after!.Version);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task SaveSchedulingSettings_DoesNotOverwriteARequestChangedSinceTheRecalculationRead(
        bool editedMeanwhile, bool windowWritten)
    {
        // The recalculation reads windows before its transaction; a user's change in between must
        // survive (spec section 5). The save is driven at repository level to place the edit exactly.
        // Its own site: a site-neutral request would count in every site's Insights and leak into
        // other tests' per-site numbers.
        var siteId = await CreateSiteAsync();
        var read = await CreateRequestAsync(new CreateRequestRequest
        {
            Name = $"Recalc-{Guid.NewGuid():N}"[..20],
            SiteId = siteId,
            MinimalDurationValue = 2,
            MinimalDurationUnit = DurationUnit.Hours,
            SchedulingSettingsApply = false,
        });
        if (editedMeanwhile)
            await SendAsync(HttpMethod.Put, $"/api/requests/{read.Id}", new UpdateRequestRequest { Name = "Edited meanwhile" }, read.Version);
        var start = DateTime.UtcNow.Date.AddDays(40).AddHours(6);

        await _scheduling.SaveSettingsAsync(
            SchedulingSettingsInfo.From(siteId, Settings("UTC")), expectedVersion: null,
            [(read.Id, read.Version, new ScheduleRequestRequest { StartTs = start, EndTs = start.AddHours(2) })]);

        var after = await _client.GetFromJsonAsync<RequestInfo>($"/api/requests/{read.Id}");
        Assert.Equal(windowWritten, after!.StartTs == start);
    }

    #endregion

    #region Helpers

    private async Task<HttpResponseMessage> SendAsync<T>(HttpMethod method, string url, T body, uint? version)
    {
        using var message = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        if (version is { } v)
            message.Headers.TryAddWithoutValidation("If-Match", $"\"{v}\"");
        return await _client.SendAsync(message);
    }

    private static async Task AssertPreconditionFailedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ApiErrorCodes.PreconditionFailed, problem.GetProperty("code").GetString());
    }

    private Task<RequestInfo> CreateRequestAsync() => CreateRequestAsync(new CreateRequestRequest
    {
        Name = $"Concurrency-{Guid.NewGuid():N}"[..20],
        MinimalDurationValue = 1,
        MinimalDurationUnit = DurationUnit.Hours,
        SchedulingSettingsApply = false,
    });

    private async Task<RequestInfo> CreateRequestAsync(CreateRequestRequest request)
    {
        var response = await _client.PostAsJsonAsync("/api/requests", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RequestInfo>())!;
    }

    private async Task<ResourceInfo> CreateExclusiveToolAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/resources", new CreateResourceRequest
        {
            ResourceTypeKey = "tool",
            Name = $"Tool-{Guid.NewGuid():N}"[..20],
            AllocationMode = "Exclusive",
        });
        return (await response.Content.ReadFromJsonAsync<ResourceInfo>())!;
    }

    private static CreateResourceAssignmentRequest Booking(Guid resourceId, Guid requestId) => new()
    {
        RequestId = requestId,
        ResourceId = resourceId,
        StartUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc),
        EndUtc = new DateTime(2026, 7, 1, 17, 0, 0, DateTimeKind.Utc),
    };

    private async Task<Guid> CreateSiteAsync()
    {
        var code = $"OC{Guid.NewGuid():N}"[..8].ToUpperInvariant();
        var response = await _client.PostAsJsonAsync("/api/sites", new { code, name = code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static UpsertSchedulingSettingsRequest Settings(string timeZone) => new() { TimeZone = timeZone };

    #endregion
}

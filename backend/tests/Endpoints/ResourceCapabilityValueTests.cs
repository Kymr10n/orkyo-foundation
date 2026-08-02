using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Models;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Criterion values on resources: type checking, the constraints from
/// <c>criteria.validation_json</c>, and proof that a user-defined type flows through the shared
/// criteria machinery.
///
/// These are the tests that survived the retirement of the parallel resource_type_fields system
/// (migration 1680) — criteria absorbed its job, so its coverage moved here rather than being
/// deleted with it.
/// </summary>
[Collection("Database collection")]
public class ResourceCapabilityValueTests
{
    private readonly HttpClient _client;

    public ResourceCapabilityValueTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    private async Task<ResourceTypeInfo> CreateCarTypeAsync() =>
        (await (await _client.PostAsJsonAsync("/api/resource-types", new CreateResourceTypeRequest
        {
            Key = $"car_{Guid.NewGuid():N}",
            DisplayName = "Car",
        })).Content.ReadFromJsonAsync<ResourceTypeInfo>())!;

    private async Task<Guid> CreateCriterionAsync(
        string typeKey, string dataType, string? validation = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = $"c_{Guid.NewGuid():N}"[..20],
            ["dataType"] = dataType,
            ["resourceTypeKeys"] = new[] { typeKey },
        };
        if (validation is not null) body["validation"] = Json(validation);

        var created = await _client.PostAsJsonAsync("/api/criteria", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CriterionInfo>())!.Id;
    }

    private async Task<Guid> CreateCarIdAsync(string typeKey, string name)
    {
        var created = await _client.PostAsJsonAsync("/api/resources", new
        {
            resourceTypeKey = typeKey,
            name,
            allocationMode = "Exclusive",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<ResourceInfo>())!.Id;
    }

    // ── User-defined types flow through criteria ──────────────────────────────

    [Fact]
    public async Task CustomType_AcceptsCriteria()
    {
        // Regression guard for retiring the hard-coded ResourceTypeKeys.IsKnown gate: a criterion
        // must be assignable to a user-defined type, and listable by that type key.
        var type = await CreateCarTypeAsync();

        var create = await _client.PostAsJsonAsync("/api/criteria", new
        {
            name = $"car_criterion_{Guid.NewGuid():N}"[..30],
            dataType = "Boolean",
            resourceTypeKeys = new[] { type.Key },
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var listed = await _client.GetAsync($"/api/criteria?resourceType={type.Key}");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
    }

    [Fact]
    public async Task Criteria_StillRejectUnknownResourceTypeKey()
    {
        var response = await _client.GetAsync("/api/criteria?resourceType=definitely_not_a_type");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Value validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task Capability_RejectsValueOfTheWrongType()
    {
        // Before criteria validated values, a Number criterion stored "banana" happily and the
        // mismatch surfaced later as a silent non-match in the solver.
        var type = await CreateCarTypeAsync();
        var criterion = await CreateCriterionAsync(type.Key, "Number", "{\"min\":0,\"max\":100}");
        var resourceId = await CreateCarIdAsync(type.Key, "Wrong-type car");

        var response = await _client.PostAsJsonAsync($"/api/resources/{resourceId}/capabilities", new
        {
            criterionId = criterion,
            value = "banana",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Capability_RejectsValueOutsideItsCriterionRange()
    {
        var type = await CreateCarTypeAsync();
        var criterion = await CreateCriterionAsync(type.Key, "Number", "{\"min\":0,\"max\":100}");
        var resourceId = await CreateCarIdAsync(type.Key, "Out-of-range car");

        var response = await _client.PostAsJsonAsync($"/api/resources/{resourceId}/capabilities", new
        {
            criterionId = criterion,
            value = 500,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Capability_AcceptsValueInsideItsCriterionRange()
    {
        var type = await CreateCarTypeAsync();
        var criterion = await CreateCriterionAsync(type.Key, "Number", "{\"min\":0,\"max\":100}");
        var resourceId = await CreateCarIdAsync(type.Key, "In-range car");

        var response = await _client.PostAsJsonAsync($"/api/resources/{resourceId}/capabilities", new
        {
            criterionId = criterion,
            value = 42,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── Date, the type criteria used to lack ──────────────────────────────────

    [Fact]
    public async Task Capability_AcceptsDateValue()
    {
        // Date was the one data type the retired field system had and criteria did not — the
        // reason criteria could not simply absorb fields until migration 1670.
        var type = await CreateCarTypeAsync();
        var criterion = await CreateCriterionAsync(type.Key, "Date");
        var resourceId = await CreateCarIdAsync(type.Key, "Dated car");

        var response = await _client.PostAsJsonAsync($"/api/resources/{resourceId}/capabilities", new
        {
            criterionId = criterion,
            value = "2026-08-02",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Capability_RejectsMalformedDate()
    {
        var type = await CreateCarTypeAsync();
        var criterion = await CreateCriterionAsync(type.Key, "Date");
        var resourceId = await CreateCarIdAsync(type.Key, "Bad-date car");

        var response = await _client.PostAsJsonAsync($"/api/resources/{resourceId}/capabilities", new
        {
            criterionId = criterion,
            value = "02-08-2026",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

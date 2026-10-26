using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkyo.Foundation.Tests.Endpoints;

[Trait(TestCategories.Name, TestCategories.Database)]
[Collection("Database collection")]
public class ConflictsEndpointTests
{
    private readonly HttpClient _client;

    public ConflictsEndpointTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.CreateAuthorizedClient();
    }

    [Theory]
    [InlineData("/api/conflicts")]
    [InlineData("/api/conflicts?from={from}&to={to}")]
    [InlineData("/api/requests?conflicted=true")]
    [InlineData("/api/requests?scheduled=false")]
    [InlineData("/api/sites/{siteId}/requests?from={from}&to={to}")]
    public async Task EachListRead_Returns200WithAnArray(string urlTemplate)
    {
        var url = urlTemplate
            .Replace("{from}", Uri.EscapeDataString(DateTime.UtcNow.AddDays(-7).ToString("o")))
            .Replace("{to}", Uri.EscapeDataString(DateTime.UtcNow.AddDays(7).ToString("o")))
            .Replace("{siteId}", Guid.NewGuid().ToString());

        var response = await _client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Services;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints.Ai;

/// <summary>
/// Saved conversations survive a reload and follow the person between devices. Nothing here
/// participates in a turn, so the shape guard matters more than the storage: a conversation
/// saved with the wrong shape would restore into a broken panel rather than fail here.
/// </summary>
[Collection("Database collection")]
public class AiConversationEndpointsTests
{
    private readonly HttpClient _client;
    private const string TenantSlug = TestConstants.TenantSlug;

    public AiConversationEndpointsTests(DatabaseFixture databaseFixture)
    {
        _client = databaseFixture.Factory.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderConstants.TenantSlug, TenantSlug);
    }

    private string? _cachedToken;

    private async Task<string> GetTokenAsync()
    {
        if (_cachedToken != null) return _cachedToken;

        var email = $"ai_convo_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email, "AI Conversation User", TenantSlug, "editor", active: true);

        var tokenData = new
        {
            UserId = userId.ToString(),
            Email = email,
            DisplayName = "AI Conversation User",
            TenantId = "00000000-0000-0000-0000-000000000001",
            TenantSlug,
            IsTenantAdmin = false,
            Role = "editor"
        };

        _cachedToken = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tokenData)));
        return _cachedToken;
    }

    private async Task<HttpRequestMessage> AuthRequest(
        HttpMethod method, string url, object? content = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync());
        if (content != null) msg.Content = JsonContent.Create(content);
        return msg;
    }

    [Theory]
    [InlineData("\"not a list\"", "[]")]
    [InlineData("[]", "{\"turn\":1}")]
    public async Task Save_WithAnEntriesOrTranscriptThatIsNotAList_Returns400(
        string entriesJson, string transcriptJson)
    {
        var body = $$"""
            {"title":"Shape check","entries":{{entriesJson}},"transcript":{{transcriptJson}}}
            """;

        var request = await AuthRequest(HttpMethod.Put, $"/api/ai/conversations/{Guid.NewGuid()}");
        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        problem.GetProperty("code").GetString()
            .Should().Be(Api.Constants.ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Save_ThenRead_ReturnsTheConversationBack()
    {
        var id = Guid.NewGuid();
        var body = """
            {"title":"Kept","entries":[{"role":"user"}],"transcript":[{"text":"hello"}]}
            """;

        var save = await AuthRequest(HttpMethod.Put, $"/api/ai/conversations/{id}");
        save.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        (await _client.SendAsync(save)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var read = await _client.SendAsync(
            await AuthRequest(HttpMethod.Get, $"/api/ai/conversations/{id}"));
        read.StatusCode.Should().Be(HttpStatusCode.OK);

        var conversation = JsonDocument.Parse(await read.Content.ReadAsStringAsync()).RootElement;
        conversation.GetProperty("title").GetString().Should().Be("Kept");
    }

    [Fact]
    public async Task Get_ForSomebodyElsesConversation_IsIndistinguishableFromOneThatIsNotThere()
    {
        var response = await _client.SendAsync(
            await AuthRequest(HttpMethod.Get, $"/api/ai/conversations/{Guid.NewGuid()}"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

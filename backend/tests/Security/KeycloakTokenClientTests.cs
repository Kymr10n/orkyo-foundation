using System.Net;
using System.Text;
using System.Text.Json;
using Api.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Orkyo.Shared.Keycloak;

namespace Orkyo.Foundation.Tests.Security;

/// <summary>
/// The one Keycloak token-endpoint parser the login exchange and the refresh share. Refresh used
/// to read the fields with <c>GetProperty</c> and throw on a missing one; login checked them.
/// </summary>
public class KeycloakTokenClientTests
{
    private static readonly KeycloakOptions Keycloak = new()
    {
        BaseUrl = "https://auth.example.com",
        Realm = "orkyo",
        BackendClientId = "orkyo-backend",
        BackendClientSecret = "secret",
    };

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parse_ReadsEveryField()
    {
        var tokens = KeycloakTokenClient.Parse(
            Json("""{"access_token":"a","refresh_token":"r","id_token":"i","expires_in":120}"""), requireIdToken: true);

        tokens.Should().Be(new Api.Endpoints.BffAuthEndpoints.TokenResponse("a", "r", "i", 120));
    }

    [Theory]
    [InlineData("""{"refresh_token":"r","id_token":"i"}""")]
    [InlineData("""{"access_token":"a","id_token":"i"}""")]
    [InlineData("""{"access_token":"a","refresh_token":"r"}""")]
    [InlineData("""{"access_token":7,"refresh_token":"r","id_token":"i"}""")]
    [InlineData("""[]""")]
    public void Parse_ALoginAnswerMissingAToken_IsNull(string json) =>
        KeycloakTokenClient.Parse(Json(json), requireIdToken: true).Should().BeNull();

    [Fact]
    public void Parse_ARefreshAnswer_NeedsNoIdToken_AndDefaultsTheLifetime()
    {
        var tokens = KeycloakTokenClient.Parse(
            Json("""{"access_token":"a","refresh_token":"r","expires_in":"soon"}"""), requireIdToken: false);

        tokens!.IdToken.Should().BeEmpty();
        tokens.ExpiresInSeconds.Should().Be(300);
    }

    private static KeycloakTokenClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond, List<string>? bodies = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(KeycloakTokenClient.HttpClientName)).Returns(new HttpClient(new StubHttpMessageHandler(req =>
        {
            bodies?.Add(req.Content!.ReadAsStringAsync().Result);
            return respond(req);
        })));
        return new KeycloakTokenClient(factory.Object, Keycloak, NullLogger<KeycloakTokenClient>.Instance);
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ExchangeCode_PostsTheCodeGrant_AndParses()
    {
        var bodies = new List<string>();
        var client = Client(_ => Answer(HttpStatusCode.OK,
            """{"access_token":"a","refresh_token":"r","id_token":"i","expires_in":60}"""), bodies);

        var tokens = await client.ExchangeCodeAsync("the-code", "verifier", "https://app/cb");

        tokens!.AccessToken.Should().Be("a");
        bodies.Single().Should().Contain("grant_type=authorization_code").And.Contain("code_verifier=verifier");
    }

    [Fact]
    public async Task Refresh_AnAnswerMissingATokenIsAFailure_NotAThrow()
    {
        var client = Client(_ => Answer(HttpStatusCode.OK, """{"access_token":"a"}"""));

        var (tokens, rejected) = await client.RefreshAsync("orkyo-backend", "secret", "old");

        tokens.Should().BeNull();
        rejected.Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""", true)]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid_client"}""", false)]
    [InlineData(HttpStatusCode.BadRequest, "not json", false)]
    [InlineData(HttpStatusCode.Unauthorized, """{"error":"invalid_grant"}""", false)]
    public async Task Refresh_OnlyInvalidGrantRejectsTheSession(HttpStatusCode status, string body, bool rejected)
    {
        var client = Client(_ => Answer(status, body));

        var result = await client.RefreshAsync("orkyo-backend", "secret", "old");

        result.Tokens.Should().BeNull();
        result.Rejected.Should().Be(rejected);
    }

    [Fact]
    public async Task ParseAsync_ANonJsonSuccessBody_IsNull()
    {
        var tokens = await KeycloakTokenClient.ParseAsync(
            Answer(HttpStatusCode.OK, "<html>"), NullLogger.Instance, requireIdToken: true);

        tokens.Should().BeNull();
    }
}

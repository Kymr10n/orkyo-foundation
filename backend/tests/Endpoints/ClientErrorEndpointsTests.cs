using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// POST /api/client-errors: anonymous, CSRF-exempt, rate-limited. The report becomes a log event
/// and nothing else, so these tests assert the contract at the boundary: what is accepted, what
/// is refused, and that no session is needed.
/// </summary>
[Collection("Database collection")]
public class ClientErrorEndpointsTests
{
    private readonly HttpClient _anonymous;
    private readonly HttpClient _signedIn;

    public ClientErrorEndpointsTests(DatabaseFixture fixture)
    {
        _anonymous = fixture.Factory.CreateClient();
        _signedIn = fixture.CreateAuthorizedClient();
    }

    private static object Error(string kind = "error", string? message = "TypeError: x is undefined") => new
    {
        kind,
        message,
        stack = "TypeError: x is undefined\n    at render (app.js:1:1)",
        route = "/app/requests",
        release = "1.4.0",
    };

    [Fact]
    public async Task AnonymousError_IsAccepted()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", Error());
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task SignedInRender_IsAccepted_WithoutACsrfHeader()
    {
        var response = await _signedIn.PostAsJsonAsync("/api/client-errors", new
        {
            kind = "render",
            message = "boom",
            componentStack = "\n    at Bomb\n    at RouteErrorBoundary",
            route = "/app/account",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task SampledVital_IsAccepted()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", new
        {
            kind = "vital",
            vitalName = "LCP",
            vitalValue = 1234.5,
            route = "/app",
            release = "1.4.0",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task UnknownKind_IsRefused()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", Error(kind: "telemetry"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()
            .Should().Contain("Kind must be one of");
    }

    [Fact]
    public async Task ErrorWithoutMessage_IsRefused()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", Error(message: ""));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task VitalWithUnknownName_IsRefused()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", new
        {
            kind = "vital",
            vitalName = "FPS",
            vitalValue = 60,
            route = "/app",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OversizedStack_IsRefused()
    {
        var response = await _anonymous.PostAsJsonAsync("/api/client-errors", new
        {
            kind = "error",
            message = "big",
            stack = new string('s', DomainLimits.ClientReportStackMaxLength + 1),
            route = "/app",
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

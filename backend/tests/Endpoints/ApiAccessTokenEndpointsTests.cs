using System.Net;
using System.Net.Http.Json;
using Api.Security;
using Api.Services.PlatformApi;
using Xunit;

namespace Orkyo.Foundation.Tests.Endpoints;

/// <summary>
/// Issuing an API access token hands an automated caller the ability to change the schedule, so
/// the surface that mints one is admin-only and its scopes are validated rather than trusted.
/// </summary>
[Collection("Database collection")]
public class ApiAccessTokenEndpointsTests
{
    private const string TokensUrl = "/api/platform/v1/tokens";

    private readonly DatabaseFixture _fixture;
    private readonly HttpClient _adminClient;

    public ApiAccessTokenEndpointsTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.CreateAuthorizedClient();
    }

    private static object NewToken(string name, params string[] scopes) =>
        new { name, scopes, expiresAt = (DateTime?)null };

    [Fact]
    public async Task CreateToken_AsAdmin_ReturnsTheRawTokenExactlyOnce()
    {
        var response = await _adminClient.PostAsJsonAsync(
            TokensUrl, NewToken("nightly agent", PlatformApiScopes.ScheduleWrite));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreatedApiAccessToken>();
        created!.RawToken.Should().StartWith("orkyo_api_");
        created.Summary.Scopes.Should().Be(PlatformApiScopes.ScheduleWrite);
        created.Summary.IsActive.Should().BeTrue();

        // The secret is never retrievable again — only the prefix identifies it afterwards.
        var list = await _adminClient.GetFromJsonAsync<List<ApiAccessTokenSummary>>(TokensUrl);
        var listed = list!.Single(t => t.Id == created.Summary.Id);
        listed.TokenPrefix.Should().Be(created.Summary.TokenPrefix);
        created.RawToken.Should().Contain(listed.TokenPrefix);
    }

    [Fact]
    public async Task CreateToken_UsesADistinctPrefixFromReportingTokens()
    {
        // The prefixes are how each auth scheme ignores the other class's credential, and how a
        // leaked string is identified by trust class at a glance.
        var api = await _adminClient.PostAsJsonAsync(
            TokensUrl, NewToken("agent", PlatformApiScopes.ScheduleRead));
        var reporting = await _adminClient.PostAsJsonAsync(
            "/api/reporting/v1/tokens", new { name = "bi" });

        var apiToken = (await api.Content.ReadFromJsonAsync<CreatedApiAccessToken>())!.RawToken;
        var reportingBody = await reporting.Content.ReadAsStringAsync();

        apiToken.Should().StartWith("orkyo_api_");
        reportingBody.Should().Contain("orkyo_rpt_");
        apiToken.Should().NotStartWith("orkyo_rpt_");
    }

    [Fact]
    public async Task CreateToken_RejectsAnUnknownScope()
    {
        var response = await _adminClient.PostAsJsonAsync(
            TokensUrl, NewToken("over-reaching agent", "tenant:admin"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateToken_RejectsAnEmptyScopeList()
    {
        var response = await _adminClient.PostAsJsonAsync(
            TokensUrl, new { name = "scopeless", scopes = Array.Empty<string>(), expiresAt = (DateTime?)null });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateToken_RejectsAnExpiryInThePast()
    {
        var response = await _adminClient.PostAsJsonAsync(TokensUrl, new
        {
            name = "already expired",
            scopes = new[] { PlatformApiScopes.ScheduleRead },
            expiresAt = DateTime.UtcNow.AddDays(-1),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeToken_MarksItInactive()
    {
        var created = await (await _adminClient.PostAsJsonAsync(
                TokensUrl, NewToken("short-lived", PlatformApiScopes.ScheduleWrite)))
            .Content.ReadFromJsonAsync<CreatedApiAccessToken>();

        var revoke = await _adminClient.DeleteAsync($"{TokensUrl}/{created!.Summary.Id}");

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await _adminClient.GetFromJsonAsync<List<ApiAccessTokenSummary>>(TokensUrl);
        list!.Single(t => t.Id == created.Summary.Id).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeToken_ThatDoesNotExist_Returns404()
    {
        var response = await _adminClient.DeleteAsync($"{TokensUrl}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TokenManagement_IsNotOpenToAnEditor()
    {
        // Editors change the schedule; issuing a credential that lets a program change it is
        // governance, so it sits with Admin — the same place reporting tokens sit.
        var editor = _fixture.CreateClientWithRole("editor");

        var list = await editor.GetAsync(TokensUrl);
        var create = await editor.PostAsJsonAsync(
            TokensUrl, NewToken("sneaky", PlatformApiScopes.ScheduleWrite));

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TokenManagement_IsNotOpenToAViewer()
    {
        var viewer = _fixture.CreateClientWithRole("viewer");

        (await viewer.GetAsync(TokensUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}

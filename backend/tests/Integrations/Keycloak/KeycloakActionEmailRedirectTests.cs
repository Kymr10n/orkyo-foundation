using System.Net;
using Api.Integrations.Keycloak;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Orkyo.Shared;
using Orkyo.Shared.Keycloak;
using Xunit;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

/// <summary>
/// Keycloak's required-actions mail must carry a client and a redirect, or the flow has
/// nowhere to send the person when they finish. Without them Keycloak ends the journey on
/// its own info page with no link, or drops them in the account console — which is where a
/// new owner landed after setting their first password, instead of in the product.
///
/// The verification mail has always passed both. These pin that the required-actions mail
/// does too, since the two sit next to each other and only one of them carried the pair.
/// </summary>
public class KeycloakActionEmailRedirectTests
{
    private const string AppUrl = "https://app.example.com";

    /// <summary>Captures every outgoing request and answers each with the minimum the caller needs.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);

            // The admin token fetch and the user lookup both happen before the mail call.
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/token", StringComparison.Ordinal)
                ? """{"access_token":"test-token","expires_in":300}"""
                : path.EndsWith("/users", StringComparison.Ordinal)
                    ? """[{"id":"user-1"}]"""
                    : "{}";

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static KeycloakAdminService CreateSut(CapturingHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ConfigKeys.AppBaseUrl] = AppUrl })
            .Build();

        var options = new KeycloakOptions
        {
            BaseUrl = "https://auth.example.com",
            InternalBaseUrl = "http://keycloak:8080",
            Realm = "orkyo",
            BackendClientId = "orkyo-backend",
            BackendClientSecret = "secret",
        };

        return new KeycloakAdminService(
            new HttpClient(handler), configuration, NullLogger<KeycloakAdminService>.Instance, options);
    }

    private static Uri ActionEmailRequest(CapturingHandler handler) =>
        handler.Requests.Single(u => u.AbsolutePath.EndsWith("/execute-actions-email", StringComparison.Ordinal));

    [Fact]
    public async Task RequiredActionsEmail_NamesTheClientAndWhereToReturn()
    {
        var handler = new CapturingHandler();

        await CreateSut(handler).SendExecuteActionsEmailAsync("owner@example.com", ["UPDATE_PASSWORD"]);

        var query = ActionEmailRequest(handler).Query;
        Assert.Contains("client_id=orkyo-backend", query, StringComparison.Ordinal);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString(AppUrl)}", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequiredActionsEmail_EscapesTheRedirect_SoTheQueryStaysOneParameter()
    {
        var handler = new CapturingHandler();

        await CreateSut(handler).SendExecuteActionsEmailAsync("owner@example.com", ["UPDATE_PASSWORD"]);

        // An unescaped "https://app..." would end the redirect_uri value at the first "/"
        // and Keycloak would reject the redirect rather than honour it.
        Assert.DoesNotContain("redirect_uri=https://", ActionEmailRequest(handler).Query, StringComparison.Ordinal);
    }
}

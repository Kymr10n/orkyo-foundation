namespace Orkyo.Foundation.Tests.Mocks;

/// <summary>
/// Programmable stand-in for Keycloak's token endpoint behind the BFF's named HttpClient.
///
/// Unset, every call fails the way the unreachable test Keycloak host did before this stub
/// existed, so a test that does not touch it sees the host it always saw. A test sets
/// <see cref="Responder"/> to complete a sign-in through the callback, and resets it afterwards.
/// </summary>
public sealed class StubKeycloakTokenEndpoint
{
    /// <summary>The answer to a token request; null = Keycloak is unreachable.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public HttpResponseMessage Respond(HttpRequestMessage request) =>
        Responder?.Invoke(request) ?? throw new HttpRequestException("Keycloak is not reachable from the test host");
}

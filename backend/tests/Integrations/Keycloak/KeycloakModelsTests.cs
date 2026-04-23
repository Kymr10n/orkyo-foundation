using System.Text.Json;
using Api.Integrations.Keycloak;
using FluentAssertions;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakModelsTests
{
    // --- KeycloakSession wire contract ---

    [Fact]
    public void KeycloakSession_Deserializes_FromKeycloakAdminWireFormat()
    {
        // Wire format owned by Keycloak Admin API GET /users/{id}/sessions.
        const string json = """
        {
            "id": "sess-123",
            "username": "alice",
            "ipAddress": "10.0.0.1",
            "start": 1700000000000,
            "lastAccess": 1700000060000,
            "clients": { "client-uuid": "saas-client" }
        }
        """;

        var session = JsonSerializer.Deserialize<KeycloakSession>(json)!;

        session.Id.Should().Be("sess-123");
        session.Username.Should().Be("alice");
        session.IpAddress.Should().Be("10.0.0.1");
        session.Start.Should().Be(1700000000000);
        session.LastAccess.Should().Be(1700000060000);
        session.Clients.Should().ContainKey("client-uuid").WhoseValue.Should().Be("saas-client");
    }

    [Fact]
    public void KeycloakSession_StartTime_ConvertsFromUnixMilliseconds()
    {
        var session = new KeycloakSession { Start = 1700000000000 };
        session.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).DateTime);
    }

    [Fact]
    public void KeycloakSession_LastAccessTime_ConvertsFromUnixMilliseconds()
    {
        var session = new KeycloakSession { LastAccess = 1700000060000 };
        session.LastAccessTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000060000).DateTime);
    }

    [Fact]
    public void KeycloakSession_Defaults_AreSafe()
    {
        var session = new KeycloakSession();
        session.Id.Should().BeEmpty();
        session.Username.Should().BeEmpty();
        session.IpAddress.Should().BeEmpty();
        session.Start.Should().Be(0);
        session.LastAccess.Should().Be(0);
        session.Clients.Should().BeNull();
    }

    // --- MfaStatus defaults ---

    [Fact]
    public void MfaStatus_Defaults_AreNotConfigured()
    {
        var status = new MfaStatus();
        status.TotpEnabled.Should().BeFalse();
        status.TotpCredentialId.Should().BeNull();
        status.TotpCreatedDate.Should().BeNull();
        status.TotpLabel.Should().BeNull();
        status.RecoveryCodesConfigured.Should().BeFalse();
        status.RecoveryCodesCredentialId.Should().BeNull();
    }

    // --- UserProfile defaults ---

    [Fact]
    public void UserProfile_Defaults_AreEmpty()
    {
        var profile = new UserProfile();
        profile.Email.Should().BeEmpty();
        profile.FirstName.Should().BeEmpty();
        profile.LastName.Should().BeEmpty();
        profile.EmailVerified.Should().BeFalse();
    }
}

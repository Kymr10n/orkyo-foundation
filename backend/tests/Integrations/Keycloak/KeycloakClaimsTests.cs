using Api.Integrations.Keycloak;
using FluentAssertions;
using Xunit;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakClaimsTests
{
    [Theory]
    [InlineData(nameof(KeycloakClaims.Subject), "sub")]
    [InlineData(nameof(KeycloakClaims.Email), "email")]
    [InlineData(nameof(KeycloakClaims.EmailVerified), "email_verified")]
    [InlineData(nameof(KeycloakClaims.PreferredUsername), "preferred_username")]
    [InlineData(nameof(KeycloakClaims.GivenName), "given_name")]
    [InlineData(nameof(KeycloakClaims.FamilyName), "family_name")]
    [InlineData(nameof(KeycloakClaims.Name), "name")]
    [InlineData(nameof(KeycloakClaims.Issuer), "iss")]
    [InlineData(nameof(KeycloakClaims.Audience), "aud")]
    [InlineData(nameof(KeycloakClaims.AuthorizedParty), "azp")]
    [InlineData(nameof(KeycloakClaims.SessionId), "sid")]
    [InlineData(nameof(KeycloakClaims.SessionState), "session_state")]
    [InlineData(nameof(KeycloakClaims.TokenType), "typ")]
    [InlineData(nameof(KeycloakClaims.Scope), "scope")]
    [InlineData(nameof(KeycloakClaims.IssuedAt), "iat")]
    [InlineData(nameof(KeycloakClaims.Expiration), "exp")]
    [InlineData(nameof(KeycloakClaims.AuthTime), "auth_time")]
    [InlineData(nameof(KeycloakClaims.RealmAccess), "realm_access")]
    public void ClaimNameConstants_AreLockedToWireValues(string memberName, string expectedWireValue)
    {
        // Drift guard: these values are part of the Keycloak JWT wire contract
        // and must not change without coordinated Keycloak realm config updates.
        var field = typeof(KeycloakClaims).GetField(memberName)!;
        field.GetRawConstantValue().Should().Be(expectedWireValue);
    }

    [Fact]
    public void SiteAdminRole_LockedToWellKnownRealmRoleName() =>
        KeycloakClaims.SiteAdminRole.Should().Be("site-admin");
}

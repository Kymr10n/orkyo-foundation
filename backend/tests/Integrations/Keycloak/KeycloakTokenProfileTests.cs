using System.Security.Claims;
using Api.Integrations.Keycloak;
using Api.Security;
using FluentAssertions;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

public class KeycloakTokenProfileTests
{
    private static ClaimsPrincipal BuildPrincipal(Dictionary<string, string> claims, bool authenticated = true)
    {
        var claimsList = claims.Select(kv => new Claim(kv.Key, kv.Value)).ToList();
        var identity = new ClaimsIdentity(claimsList, authenticated ? "Bearer" : null);
        return new ClaimsPrincipal(identity);
    }

    private static string RealmAccessJson(params string[] roles)
    {
        var rolesJson = string.Join(",", roles.Select(r => $"\"{r}\""));
        return $"{{\"roles\":[{rolesJson}]}}";
    }

    // --- IsValid / IsAuthenticated ---

    [Fact]
    public void IsValid_ShouldBeTrue_WhenSubjectPresent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "user-123" }));
        profile.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldBeFalse_WhenSubjectMissing()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["email"] = "a@b.com" }));
        profile.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsAuthenticated_ShouldBeTrue_WhenIdentityAuthenticated()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "user-123" }, authenticated: true));
        profile.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public void IsAuthenticated_ShouldBeFalse_WhenUnauthenticatedPrincipal()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "user-123" }, authenticated: false));
        profile.IsAuthenticated.Should().BeFalse();
    }

    // --- Claim extraction ---

    [Fact]
    public void EmailVerified_ShouldBeTrue_WhenClaimIsTrue()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "u", ["email_verified"] = "True" }));
        profile.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public void EmailVerified_ShouldBeFalse_WhenClaimAbsent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "u" }));
        profile.EmailVerified.Should().BeFalse();
    }

    // --- DisplayName fallback chain ---

    [Fact]
    public void DisplayName_ShouldPreferNameClaim()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["name"] = "Full Name",
            ["given_name"] = "Given",
            ["family_name"] = "Family",
            ["preferred_username"] = "username"
        }));
        profile.DisplayName.Should().Be("Full Name");
    }

    [Fact]
    public void DisplayName_ShouldFallbackToGivenPlusFamilyWhenNameAbsent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["given_name"] = "Alice",
            ["family_name"] = "Smith",
            ["preferred_username"] = "asmith"
        }));
        profile.DisplayName.Should().Be("Alice Smith");
    }

    [Fact]
    public void DisplayName_ShouldFallbackToPreferredUsername()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["preferred_username"] = "asmith"
        }));
        profile.DisplayName.Should().Be("asmith");
    }

    [Fact]
    public void DisplayName_ShouldFallbackToEmailLocalPart()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["email"] = "alice@example.com"
        }));
        profile.DisplayName.Should().Be("alice");
    }

    [Fact]
    public void DisplayName_ShouldBeNull_WhenNoClaimsPresent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "u" }));
        profile.DisplayName.Should().BeNull();
    }

    // --- Realm roles ---

    [Fact]
    public void RealmRoles_ShouldReturnEmpty_WhenClaimAbsent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["sub"] = "u" }));
        profile.RealmRoles.Should().BeEmpty();
    }

    [Fact]
    public void RealmRoles_ShouldParseRolesFromJson()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["realm_access"] = RealmAccessJson("site-admin", "user")
        }));
        profile.RealmRoles.Should().BeEquivalentTo(new[] { "site-admin", "user" });
    }

    [Fact]
    public void IsSiteAdmin_ShouldBeTrue_WhenRolePresent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["realm_access"] = RealmAccessJson("site-admin")
        }));
        profile.IsSiteAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsSiteAdmin_ShouldBeFalse_WhenRoleAbsent()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["realm_access"] = RealmAccessJson("user")
        }));
        profile.IsSiteAdmin.Should().BeFalse();
    }

    [Fact]
    public void RealmRoles_ShouldReturnEmpty_WhenRealmAccessJsonIsInvalid()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["realm_access"] = "not-json"
        }));
        profile.RealmRoles.Should().BeEmpty();
    }

    [Fact]
    public void RealmRoles_ShouldReturnEmpty_WhenNoRolesProperty()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "u",
            ["realm_access"] = "{\"other\":[]}"
        }));
        profile.RealmRoles.Should().BeEmpty();
    }

    // --- ToExternalIdentityToken ---

    [Fact]
    public void ToExternalIdentityToken_ShouldReturnNull_WhenSubjectMissing()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(
            BuildPrincipal(new() { ["email"] = "a@b.com" }));
        profile.ToExternalIdentityToken().Should().BeNull();
    }

    [Fact]
    public void ToExternalIdentityToken_ShouldMapFieldsCorrectly()
    {
        var profile = KeycloakTokenProfile.FromPrincipal(BuildPrincipal(new()
        {
            ["sub"] = "user-abc",
            ["email"] = "alice@example.com",
            ["email_verified"] = "True",
            ["name"] = "Alice",
            ["iss"] = "https://auth.example.com/realms/orkyo",
            ["aud"] = "saas-client"
        }));

        var token = profile.ToExternalIdentityToken();

        token.Should().NotBeNull();
        token!.Provider.Should().Be(AuthProvider.Keycloak);
        token.Subject.Should().Be("user-abc");
        token.Email.Should().Be("alice@example.com");
        token.EmailVerified.Should().BeTrue();
        token.DisplayName.Should().Be("Alice");
        token.Issuer.Should().Be("https://auth.example.com/realms/orkyo");
        token.Audience.Should().Be("saas-client");
    }
}

using Api.Models;

namespace Orkyo.Foundation.Tests.Models;

public class AuthModelsTests
{
    [Fact]
    public void User_ShouldInitializeWithRequiredProperties()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Status = UserStatus.Active,
            IsTenantAdmin = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("Test User", user.DisplayName);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.IsTenantAdmin);
    }

    [Fact]
    public void User_ShouldSupportKeycloakIntegration()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Status = UserStatus.Active,
            IsTenantAdmin = false,
            KeycloakId = "keycloak-sub-12345",
            KeycloakMetadata = @"{""groups"": [""users"", ""admins""], ""roles"": [""user""]}"
        };

        Assert.Equal("keycloak-sub-12345", user.KeycloakId);
        Assert.NotNull(user.KeycloakMetadata);
        Assert.Contains("groups", user.KeycloakMetadata);
    }

    [Fact]
    public void User_KeycloakPropertiesAreNullable()
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            DisplayName = "Test User",
            Status = UserStatus.Active,
            IsTenantAdmin = false
        };

        Assert.Null(user.KeycloakId);
        Assert.Null(user.KeycloakMetadata);
    }

    [Fact]
    public void UserStatus_ShouldHaveExpectedValues()
    {
        Assert.Equal("Active", UserStatus.Active.ToString());
        Assert.Equal("Disabled", UserStatus.Disabled.ToString());
        Assert.Equal("PendingVerification", UserStatus.PendingVerification.ToString());
    }

    [Fact]
    public void TenantStatus_ShouldHaveExpectedValues()
    {
        Assert.Equal("Active", TenantStatus.Active.ToString());
        Assert.Equal("Suspended", TenantStatus.Suspended.ToString());
        Assert.Equal("Deleting", TenantStatus.Deleting.ToString());
    }

    [Fact]
    public void Tenant_ShouldInitializeWithRequiredProperties()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Slug = "test-tenant",
            DisplayName = "Test Tenant",
            Status = TenantStatus.Active,
            DbIdentifier = "orkyo_tenant_test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Assert.NotEqual(Guid.Empty, tenant.Id);
        Assert.Equal("test-tenant", tenant.Slug);
        Assert.Equal("Test Tenant", tenant.DisplayName);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal("orkyo_tenant_test", tenant.DbIdentifier);
    }

    [Fact]
    public void UserIdentity_ShouldInitializeWithRequiredProperties()
    {
        var userId = Guid.NewGuid();
        var identity = new UserIdentity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Provider = "google",
            ProviderSubject = "google-user-123",
            ProviderEmail = "user@gmail.com",
            CreatedAt = DateTime.UtcNow
        };

        Assert.NotEqual(Guid.Empty, identity.Id);
        Assert.Equal(userId, identity.UserId);
        Assert.Equal("google", identity.Provider);
        Assert.Equal("google-user-123", identity.ProviderSubject);
        Assert.Equal("user@gmail.com", identity.ProviderEmail);
    }
}

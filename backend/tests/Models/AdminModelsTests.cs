using Api.Models.Admin;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the 0%-coverage admin model types in <c>Models/Admin/UserAdminModels.cs</c>.
/// </summary>
public class AdminModelsTests
{
    // ── AdminUserSummary ───────────────────────────────────────────────────

    [Fact]
    public void AdminUserSummary_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var created = DateTime.UtcNow;

        var summary = new AdminUserSummary
        {
            Id = id,
            Email = "admin@example.com",
            DisplayName = "Alice Admin",
            Status = "active",
            CreatedAt = created,
            UpdatedAt = created,
            LastLoginAt = created,
            MembershipCount = 3,
            IdentityCount = 2,
            IsSiteAdmin = true,
            OwnedTenantId = tenantId,
            OwnedTenantTier = "premium"
        };

        summary.Id.Should().Be(id);
        summary.Email.Should().Be("admin@example.com");
        summary.DisplayName.Should().Be("Alice Admin");
        summary.Status.Should().Be("active");
        summary.MembershipCount.Should().Be(3);
        summary.IdentityCount.Should().Be(2);
        summary.IsSiteAdmin.Should().BeTrue();
        summary.OwnedTenantId.Should().Be(tenantId);
        summary.OwnedTenantTier.Should().Be("premium");
    }

    [Fact]
    public void AdminUserSummary_OptionalFields_AreNullByDefault()
    {
        var summary = new AdminUserSummary
        {
            Id = Guid.NewGuid(),
            Email = "user@example.com",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MembershipCount = 0,
            IdentityCount = 1,
            IsSiteAdmin = false
        };

        summary.DisplayName.Should().BeNull();
        summary.LastLoginAt.Should().BeNull();
        summary.OwnedTenantId.Should().BeNull();
        summary.OwnedTenantTier.Should().BeNull();
    }

    // ── AdminUserDetail ────────────────────────────────────────────────────

    [Fact]
    public void AdminUserDetail_StoresAllFields()
    {
        var userId = Guid.NewGuid();
        var identityId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var identity = new AdminUserIdentity
        {
            Id = identityId,
            Provider = "keycloak",
            ProviderSubject = "auth|abc123",
            ProviderEmail = "alice@example.com",
            CreatedAt = now
        };

        var membership = new AdminUserMembership
        {
            TenantId = tenantId,
            TenantSlug = "acme",
            TenantName = "Acme Corp",
            Role = "admin",
            Status = "active",
            JoinedAt = now
        };

        var detail = new AdminUserDetail
        {
            Id = userId,
            Email = "alice@example.com",
            DisplayName = "Alice",
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
            LastLoginAt = now,
            IsSiteAdmin = false,
            OwnedTenantId = null,
            OwnedTenantTier = null,
            Identities = new List<AdminUserIdentity> { identity },
            Memberships = new List<AdminUserMembership> { membership }
        };

        detail.Id.Should().Be(userId);
        detail.Identities.Should().HaveCount(1);
        detail.Memberships.Should().HaveCount(1);
        detail.Memberships[0].TenantSlug.Should().Be("acme");
    }

    // ── AdminUserIdentity ──────────────────────────────────────────────────

    [Fact]
    public void AdminUserIdentity_StoresAllFields()
    {
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var identity = new AdminUserIdentity
        {
            Id = id,
            Provider = "google",
            ProviderSubject = "google|xyz",
            ProviderEmail = "alice@gmail.com",
            CreatedAt = now
        };

        identity.Provider.Should().Be("google");
        identity.ProviderSubject.Should().Be("google|xyz");
        identity.ProviderEmail.Should().Be("alice@gmail.com");
    }

    [Fact]
    public void AdminUserIdentity_ProviderEmail_IsOptional()
    {
        var identity = new AdminUserIdentity
        {
            Id = Guid.NewGuid(),
            Provider = "saml",
            ProviderSubject = "saml|001",
            CreatedAt = DateTime.UtcNow
        };

        identity.ProviderEmail.Should().BeNull();
    }

    // ── AdminUserMembership ────────────────────────────────────────────────

    [Fact]
    public void AdminUserMembership_StoresAllFields()
    {
        var tenantId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var membership = new AdminUserMembership
        {
            TenantId = tenantId,
            TenantSlug = "bigcorp",
            TenantName = "Big Corp Ltd.",
            Role = "viewer",
            Status = "pending",
            JoinedAt = now
        };

        membership.TenantId.Should().Be(tenantId);
        membership.TenantSlug.Should().Be("bigcorp");
        membership.Role.Should().Be("viewer");
        membership.Status.Should().Be("pending");
    }
}

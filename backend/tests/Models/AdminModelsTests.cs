using Api.Models.Admin;

namespace Orkyo.Foundation.Tests.Models;

/// <summary>
/// Covers the 0%-coverage admin model types in <c>Models/Admin/UserAdminModels.cs</c>.
/// </summary>
public class AdminModelsTests
{
    // ── AdminUserSummary ───────────────────────────────────────────────────

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

    // ── AdminUserIdentity ──────────────────────────────────────────────────

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
}

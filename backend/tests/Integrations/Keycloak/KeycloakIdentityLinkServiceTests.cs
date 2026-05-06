using Api.Helpers;
using Api.Integrations.Keycloak;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integrations.Keycloak;

/// <summary>
/// Integration tests for <see cref="KeycloakIdentityLinkService"/>.
/// Uses the shared PostgreSQL test container via <see cref="DatabaseFixture"/>.
/// </summary>
[Collection("Database collection")]
public class KeycloakIdentityLinkServiceTests
{
    private readonly DatabaseFixture _fixture;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly Mock<IEmailService> _emailService;

    public KeycloakIdentityLinkServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _dbFactory = fixture.Factory.Services.GetRequiredService<IDbConnectionFactory>();
        _emailService = new Mock<IEmailService>();
        _emailService.Setup(e => e.SendNewUserAlertAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
    }

    private KeycloakIdentityLinkService CreateService() =>
        new(_dbFactory, _emailService.Object, NullLogger<KeycloakIdentityLinkService>.Instance);

    // ── FindByExternalIdentityAsync ────────────────────────────────────────

    [Fact]
    public async Task FindByExternalIdentity_ReturnsNull_WhenUnsupportedProvider()
    {
        var svc = CreateService();

        var result = await svc.FindByExternalIdentityAsync(AuthProvider.Keycloak + 999, "any-sub");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByExternalIdentity_ReturnsNull_WhenSubjectNotLinked()
    {
        var svc = CreateService();

        var result = await svc.FindByExternalIdentityAsync(AuthProvider.Keycloak, Guid.NewGuid().ToString());

        result.Should().BeNull();
    }

    // ── LinkIdentityAsync — guard clauses ─────────────────────────────────

    [Fact]
    public async Task LinkIdentity_ReturnsFailure_WhenProviderIsUnsupported()
    {
        var svc = CreateService();

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak + 999,
            Subject = "sub",
            Email = "u@example.com"
        };

        var result = await svc.LinkIdentityAsync(token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProblemDetailsHelper.AuthCodes.InvalidToken);
    }

    [Fact]
    public async Task LinkIdentity_ReturnsFailure_WhenSubjectIsEmpty()
    {
        var svc = CreateService();

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = string.Empty,
            Email = "u@example.com"
        };

        var result = await svc.LinkIdentityAsync(token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProblemDetailsHelper.AuthCodes.InvalidToken);
    }

    // ── LinkIdentityAsync — full flows ────────────────────────────────────

    [Fact]
    public async Task LinkIdentity_CreatesNewUser_WhenNoExistingUserOrIdentity()
    {
        var svc = CreateService();
        var uniqueSub = $"kc-new-{Guid.NewGuid():N}";
        var uniqueEmail = $"new-{Guid.NewGuid():N}@example.com";

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = uniqueSub,
            Email = uniqueEmail,
            EmailVerified = true,
            DisplayName = "New Auto User"
        };

        var result = await svc.LinkIdentityAsync(token);

        result.Success.Should().BeTrue();
        result.IsNewUser.Should().BeTrue();
        result.Email.Should().Be(uniqueEmail);
        result.UserId.Should().NotBeNull().And.NotBe(Guid.Empty);
        _emailService.Verify(e => e.SendNewUserAlertAsync(uniqueEmail, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task LinkIdentity_LinksToExistingUser_WhenEmailMatchesInvitedUser()
    {
        var svc = CreateService();
        var userId = Guid.NewGuid();
        var uniqueEmail = $"invited-{Guid.NewGuid():N}@example.com";
        var uniqueSub = $"kc-invited-{Guid.NewGuid():N}";

        // Pre-insert a user with no identity link (simulating an invitation)
        await using var conn = _dbFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var insertUser = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status, created_at, updated_at) VALUES (@id, @email, 'Invited User', 'active', NOW(), NOW())",
            conn);
        insertUser.Parameters.AddWithValue("id", userId);
        insertUser.Parameters.AddWithValue("email", uniqueEmail);
        await insertUser.ExecuteNonQueryAsync();

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = uniqueSub,
            Email = uniqueEmail,
            EmailVerified = true
        };

        var result = await svc.LinkIdentityAsync(token);

        result.Success.Should().BeTrue();
        result.IsNewUser.Should().BeFalse();
        result.UserId.Should().Be(userId);

        // Cleanup
        await using var cleanupLink = new NpgsqlCommand(
            "DELETE FROM user_identities WHERE external_subject = @sub", conn);
        cleanupLink.Parameters.AddWithValue("sub", uniqueSub);
        await cleanupLink.ExecuteNonQueryAsync();

        await using var cleanupUser = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
        cleanupUser.Parameters.AddWithValue("id", userId);
        await cleanupUser.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LinkIdentity_ReturnsFailure_WhenEmailMatchesInactiveUser()
    {
        var svc = CreateService();
        var userId = Guid.NewGuid();
        var uniqueEmail = $"disabled-{Guid.NewGuid():N}@example.com";
        var uniqueSub = $"kc-disabled-{Guid.NewGuid():N}";

        await using var conn = _dbFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var insertUser = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status, created_at, updated_at) VALUES (@id, @email, 'Disabled User', 'disabled', NOW(), NOW())",
            conn);
        insertUser.Parameters.AddWithValue("id", userId);
        insertUser.Parameters.AddWithValue("email", uniqueEmail);
        await insertUser.ExecuteNonQueryAsync();

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = uniqueSub,
            Email = uniqueEmail
        };

        var result = await svc.LinkIdentityAsync(token);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ProblemDetailsHelper.AuthCodes.AccountInactive);

        // Cleanup
        await using var cleanupUser = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
        cleanupUser.Parameters.AddWithValue("id", userId);
        await cleanupUser.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task LinkIdentity_IsIdempotent_WhenCalledTwiceWithSameIdentity()
    {
        var svc = CreateService();
        var uniqueSub = $"kc-idempotent-{Guid.NewGuid():N}";
        var uniqueEmail = $"idempotent-{Guid.NewGuid():N}@example.com";

        var token = new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = uniqueSub,
            Email = uniqueEmail,
            EmailVerified = true
        };

        var first = await svc.LinkIdentityAsync(token);
        var second = await svc.LinkIdentityAsync(token);

        first.Success.Should().BeTrue();
        first.IsNewUser.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.IsNewUser.Should().BeFalse();
        second.UserId.Should().Be(first.UserId);
    }

    // ── GetUserMembershipsAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetUserMembershipsAsync_ReturnsActiveMemberships()
    {
        var svc = CreateService();

        var memberships = await svc.GetUserMembershipsAsync(
            new Guid("11111111-1111-1111-1111-111111111111"));

        memberships.Should().NotBeNull();
        memberships.Should().NotBeEmpty();
        memberships.Should().AllSatisfy(m =>
        {
            m.TenantId.Should().NotBe(Guid.Empty);
            m.TenantSlug.Should().NotBeNullOrEmpty();
            m.Role.Should().NotBe(TenantRole.None);
        });
    }

    [Fact]
    public async Task GetUserMembershipsAsync_ReturnsEmpty_WhenUserHasNoMemberships()
    {
        var svc = CreateService();

        var memberships = await svc.GetUserMembershipsAsync(Guid.NewGuid());

        memberships.Should().BeEmpty();
    }

    // ── GetUserTenantRoleAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetUserTenantRoleAsync_ReturnsNone_WhenUserNotMember()
    {
        var svc = CreateService();

        var role = await svc.GetUserTenantRoleAsync(Guid.NewGuid(), Guid.NewGuid());

        role.Should().Be(TenantRole.None);
    }

    [Fact]
    public async Task GetUserTenantRoleAsync_ReturnsRole_WhenUserIsMember()
    {
        var svc = CreateService();

        // The seeded admin user is a member of the test tenant
        await using var conn = _dbFactory.CreateControlPlaneConnection();
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM tenants WHERE slug = @slug LIMIT 1", conn);
        cmd.Parameters.AddWithValue("slug", TestConstants.TenantSlug);
        var tenantId = (Guid?)await cmd.ExecuteScalarAsync();

        if (tenantId == null)
        {
            // Tenant not seeded, skip
            return;
        }

        var role = await svc.GetUserTenantRoleAsync(
            new Guid("11111111-1111-1111-1111-111111111111"),
            tenantId.Value);

        role.Should().NotBe(TenantRole.None);
    }
}

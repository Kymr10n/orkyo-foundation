using Api.Integrations.Keycloak;
using Api.Security;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// DB-backed tests for foundation-owned <see cref="KeycloakIdentityLinkService"/>
/// using an in-foundation test connection factory. This is the authentication
/// entry path — every Keycloak login flows through <c>LinkIdentityAsync</c>. The
/// tests cover the three branches: already-linked, match-by-email (invitation flow),
/// and auto-provision new user (self-registration).
///
/// Keycloak's HTTP admin API is NOT exercised here — the IEmailService dependency is
/// mocked, and only the DB side of identity-link is asserted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class KeycloakIdentityLinkServiceIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public KeycloakIdentityLinkServiceIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    // ── FindByExternalIdentityAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindByExternalIdentity_ReturnsNull_WhenSubjectNotLinked()
    {
        var service = BuildService();

        var result = await service.FindByExternalIdentityAsync(AuthProvider.Keycloak, $"kc-{Guid.NewGuid():N}");

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindByExternalIdentity_ReturnsPrincipal_WhenSubjectLinkedToActiveUser()
    {
        var service = BuildService();
        var email = UniqueEmail();
        var subject = UniqueSubject();
        var userId = await CreateUserAsync(email, displayName: "Alice", status: "active");
        await CreateIdentityLinkAsync(userId, subject, email);

        var result = await service.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject);

        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.Email.Should().Be(email);
        result.DisplayName.Should().Be("Alice");
        result.ExternalSubject.Should().Be(subject);
        result.AuthProvider.Should().Be(AuthProvider.Keycloak);
    }

    [Fact]
    public async Task FindByExternalIdentity_ReturnsNull_ForDisabledUser()
    {
        var service = BuildService();
        var email = UniqueEmail();
        var subject = UniqueSubject();
        var userId = await CreateUserAsync(email, displayName: null, status: "disabled");
        await CreateIdentityLinkAsync(userId, subject, email);

        var result = await service.FindByExternalIdentityAsync(AuthProvider.Keycloak, subject);

        result.Should().BeNull("the lookup filters on users.status = 'active'");
    }

    [Fact]
    public async Task FindByExternalIdentity_ReturnsNull_ForNonKeycloakProvider()
    {
        var service = BuildService();

        var result = await service.FindByExternalIdentityAsync(AuthProvider.Local, "any-subject");

        result.Should().BeNull();
    }

    // ── LinkIdentityAsync — three branches ───────────────────────────────────

    [Fact]
    public async Task LinkIdentity_ExistingLink_ReturnsLinkedNotNew_AndUpdatesLastLogin()
    {
        var service = BuildService();
        var email = UniqueEmail();
        var subject = UniqueSubject();
        var userId = await CreateUserAsync(email, displayName: "Bob", status: "active");
        await CreateIdentityLinkAsync(userId, subject, email);
        var before = await ReadLastLoginAsync(userId);

        var result = await service.LinkIdentityAsync(BuildToken(subject, email, "Bob"));

        result.Success.Should().BeTrue();
        result.IsNewUser.Should().BeFalse();
        result.UserId.Should().Be(userId);
        var after = await ReadLastLoginAsync(userId);
        after.Should().NotBeNull();
        if (before.HasValue) after!.Value.Should().BeOnOrAfter(before.Value);
    }

    [Fact]
    public async Task LinkIdentity_MatchByEmail_LinksToExistingUser_AndReturnsNotNew()
    {
        var service = BuildService();
        var email = UniqueEmail();
        var subject = UniqueSubject();
        var userId = await CreateUserAsync(email, displayName: "Carol", status: "active");
        // deliberately no pre-existing identity link — simulates invitation flow

        var result = await service.LinkIdentityAsync(BuildToken(subject, email, "Carol"));

        result.Success.Should().BeTrue();
        result.IsNewUser.Should().BeFalse();
        result.UserId.Should().Be(userId);
        (await IdentityLinkExistsAsync(subject)).Should().BeTrue();
    }

    [Fact]
    public async Task LinkIdentity_MatchByEmail_RejectsInactiveUser()
    {
        var service = BuildService();
        var email = UniqueEmail();
        await CreateUserAsync(email, displayName: null, status: "disabled");

        var result = await service.LinkIdentityAsync(BuildToken(UniqueSubject(), email, null));

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("account_inactive");
    }

    [Fact]
    public async Task LinkIdentity_AutoProvisionsNewUser_WhenNoMatch_AndReturnsIsNewTrue()
    {
        var emailServiceMock = new Mock<IEmailService>(MockBehavior.Loose);
        var service = BuildService(emailServiceMock.Object);
        var email = UniqueEmail();
        var subject = UniqueSubject();

        var result = await service.LinkIdentityAsync(BuildToken(subject, email, "NewUser"));

        result.Success.Should().BeTrue();
        result.IsNewUser.Should().BeTrue();
        result.UserId.Should().NotBeEmpty();
        (await UserExistsWithEmailAsync(email)).Should().BeTrue();
        (await IdentityLinkExistsAsync(subject)).Should().BeTrue();
    }

    [Fact]
    public async Task LinkIdentity_RejectsUnsupportedProvider()
    {
        var service = BuildService();

        var result = await service.LinkIdentityAsync(new ExternalIdentityToken
        {
            Provider = AuthProvider.Local,
            Subject = "any",
            Email = UniqueEmail(),
            EmailVerified = true,
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_token");
    }

    [Fact]
    public async Task LinkIdentity_RejectsEmptySubject()
    {
        var service = BuildService();

        var result = await service.LinkIdentityAsync(new ExternalIdentityToken
        {
            Provider = AuthProvider.Keycloak,
            Subject = "",
            Email = UniqueEmail(),
            EmailVerified = true,
        });

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("invalid_token");
    }

    // ── composition ──────────────────────────────────────────────────────────

    private KeycloakIdentityLinkService BuildService(IEmailService? emailService = null)
    {
        var factory = _fixture.CreateConnectionFactory();
        return new KeycloakIdentityLinkService(
            factory,
            emailService ?? Mock.Of<IEmailService>(),
            NullLogger<KeycloakIdentityLinkService>.Instance);
    }


    private static ExternalIdentityToken BuildToken(string subject, string email, string? displayName) =>
        new()
        {
            Provider = AuthProvider.Keycloak,
            Subject = subject,
            Email = email,
            EmailVerified = true,
            DisplayName = displayName,
        };

    // ── DB helpers ───────────────────────────────────────────────────────────

    private static string UniqueEmail() => $"kil-{Guid.NewGuid():N}@example.com";
    private static string UniqueSubject() => $"kc-sub-{Guid.NewGuid():N}";

    private async Task<Guid> CreateUserAsync(string email, string? displayName, string status)
    {
        var userId = Guid.NewGuid();
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO users (id, email, display_name, status) VALUES (@id, @email, @name, @status)", conn);
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("name", (object?)displayName ?? "Test User");
        cmd.Parameters.AddWithValue("status", status);
        await cmd.ExecuteNonQueryAsync();
        return userId;
    }

    private async Task CreateIdentityLinkAsync(Guid userId, string subject, string email)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_identities (id, user_id, provider, provider_subject, provider_email) " +
            "VALUES (@id, @u, 'keycloak', @sub, @email)", conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("sub", subject);
        cmd.Parameters.AddWithValue("email", email);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<DateTime?> ReadLastLoginAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT last_login_at FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : (DateTime)result;
    }

    private async Task<bool> IdentityLinkExistsAsync(string subject)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM user_identities WHERE provider = 'keycloak' AND provider_subject = @s)",
            conn);
        cmd.Parameters.AddWithValue("s", subject);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }

    private async Task<bool> UserExistsWithEmailAsync(string email)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM users WHERE LOWER(email) = LOWER(@e))", conn);
        cmd.Parameters.AddWithValue("e", email);
        return (bool)(await cmd.ExecuteScalarAsync() ?? false);
    }
}

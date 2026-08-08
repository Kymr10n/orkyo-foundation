using Api.Integrations.Keycloak;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Orkyo.Foundation.Tests.Integration;

/// <summary>
/// The one place that turns an email address into a control-plane account. Two callers
/// depend on it — accepting an invitation and a site admin opening a workspace — so the
/// awkward cases (case folding, concurrent inserts, an identity provider that already
/// knows the address) are pinned here rather than in either caller.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserProvisioningServiceIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public UserProvisioningServiceIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CreatesAccount_WhenAddressIsUnknown()
    {
        var keycloak = new MockKeycloakAdminService();
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        var result = await service.ResolveOrCreateAsync(conn, null, email, displayName: "New Owner");

        result.Created.Should().BeTrue();
        result.UserId.Should().NotBeEmpty();
        keycloak.CreateUserCallCount.Should().Be(1);
        (await ReadEmailAsync(result.UserId)).Should().Be(email.ToLowerInvariant());
    }

    [Fact]
    public async Task ReturnsExistingAccount_RegardlessOfCase_AndDoesNotTouchKeycloak()
    {
        var keycloak = new MockKeycloakAdminService();
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        var first = await service.ResolveOrCreateAsync(conn, null, email);

        var second = await service.ResolveOrCreateAsync(conn, null, email.ToUpperInvariant());

        second.Created.Should().BeFalse();
        second.UserId.Should().Be(first.UserId, "one mailbox is one account whatever the spelling");
        keycloak.CreateUserCallCount.Should().Be(1);
    }

    [Fact]
    public async Task StoresLowercase_SoTheUniqueIndexAndTheLookupAgree()
    {
        // users_email_key is case-sensitive while the lookup is not; storing mixed case
        // would let two spellings of one mailbox both insert.
        var keycloak = new MockKeycloakAdminService();
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail().ToUpperInvariant();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        var result = await service.ResolveOrCreateAsync(conn, null, email);

        (await ReadEmailAsync(result.UserId)).Should().Be(email.ToLowerInvariant());
    }

    [Fact]
    public async Task ToleratesAConcurrentWinner_InsteadOfThrowingOnTheUniqueIndex()
    {
        var keycloak = new MockKeycloakAdminService();
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        // Somebody else inserted the row between our lookup and our write.
        var winnerId = await InsertUserAsync(email);

        var result = await service.ResolveOrCreateAsync(conn, null, email);

        result.UserId.Should().Be(winnerId);
        result.Created.Should().BeFalse();
    }

    [Fact]
    public async Task TreatsAnExistingKeycloakAccountAsUsable()
    {
        // The control plane has no row but the identity provider already knows the
        // address — an interrupted earlier attempt. Recover rather than fail.
        // The mock maps an "already exists" message to 409, same as the real client.
        var keycloak = new MockKeycloakAdminService
        {
            CreateUserSuccess = false,
            CreateUserError = "An account with this email already exists"
        };
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        // password: null — we generated the credential, so reusing the account is safe.
        var result = await service.ResolveOrCreateAsync(conn, null, email, password: null);

        result.Created.Should().BeTrue();
        (await ReadEmailAsync(result.UserId)).Should().Be(email);
    }

    [Fact]
    public async Task SurfacesAnExistingKeycloakAccount_WhenTheCallerSuppliedAPassword()
    {
        // The invitee is choosing a password right now. Keycloak declining to set it
        // means acceptance has to fail — otherwise they get an account whose password
        // is not the one they just typed.
        var keycloak = new MockKeycloakAdminService
        {
            CreateUserSuccess = false,
            CreateUserError = "An account with this email already exists"
        };
        var service = new UserProvisioningService(keycloak, NullLogger<UserProvisioningService>.Instance);

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();

        var act = () => service.ResolveOrCreateAsync(
            conn, null, UniqueEmail(), password: "TheyJustTypedThis1!");

        await act.Should().ThrowAsync<KeycloakAdminException>();
    }

    [Fact]
    public async Task RejectsAnAddressThatIsNotAnEmail()
    {
        var service = new UserProvisioningService(
            new MockKeycloakAdminService(), NullLogger<UserProvisioningService>.Instance);

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();

        var act = () => service.ResolveOrCreateAsync(conn, null, "not-an-email");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RollsBackWithTheCallersTransaction()
    {
        var service = new UserProvisioningService(
            new MockKeycloakAdminService(), NullLogger<UserProvisioningService>.Instance);
        var email = UniqueEmail();

        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await service.ResolveOrCreateAsync(conn, tx, email);
            await tx.RollbackAsync();
        }

        (await UserExistsAsync(email)).Should().BeFalse(
            "the users row must live and die with whatever the caller was writing");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string UniqueEmail() => $"prov-{Guid.NewGuid():N}@example.test";

    private async Task<string?> ReadEmailAsync(Guid userId)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT email FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        return await cmd.ExecuteScalarAsync() as string;
    }

    private async Task<bool> UserExistsAsync(string email)
    {
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM users WHERE LOWER(email) = LOWER(@e)", conn);
        cmd.Parameters.AddWithValue("e", email);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task<Guid> InsertUserAsync(string email)
    {
        var id = Guid.NewGuid();
        await using var conn = await _fixture.OpenControlPlaneConnectionAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO users (id, email, display_name, status, created_at, updated_at)
            VALUES (@id, @email, @email, 'active', NOW(), NOW())", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email.ToLowerInvariant());
        await cmd.ExecuteNonQueryAsync();
        return id;
    }
}

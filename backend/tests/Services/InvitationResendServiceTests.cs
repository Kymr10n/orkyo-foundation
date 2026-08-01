using Api.Constants;
using Api.Integrations.Keycloak;
using Api.Models;
using Api.Security.Quotas;
using Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orkyo.Shared;
using Xunit;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// DB-backed tests for <see cref="InvitationService.ResendInvitationAsync"/>.
///
/// <para>These are service-level, not endpoint-level, on purpose:
/// <see cref="FoundationWebApplicationFactory"/> registers
/// <c>Mock.Of&lt;IInvitationService&gt;()</c>, so an HTTP test can only ever observe the mock's
/// default <c>false</c> — it can prove the route exists and is auth-gated, and nothing more. All
/// the behaviour worth guarding (token rotation, expiry reset, the accepted guard, tenant
/// scoping) lives in the SQL, so it is exercised against a real database here.</para>
/// </summary>
[Collection("Database collection")]
public sealed class InvitationResendServiceTests
{
    private readonly DatabaseFixture _fixture;
    private readonly string _connString;

    private static readonly Guid TestTenantId = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TestUserId = new("11111111-1111-1111-1111-111111111111");

    public InvitationResendServiceTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _connString = $"Host=localhost;Port={fixture.DatabasePort};Database=control_plane;Username=postgres;Password=postgres";
    }

    private (InvitationService Service, Mock<IEmailService> Email, Mock<ITenantUserService> TenantUsers) BuildService()
    {
        var settings = new Mock<ITenantSettingsService>();
        settings.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSettings());

        var email = new Mock<IEmailService>();
        var tenantUsers = new Mock<ITenantUserService>();

        var service = new InvitationService(
            new SingleTenantDbConnectionFactory(_connString),
            email.Object,
            tenantUsers.Object,
            Mock.Of<IKeycloakAdminService>(),
            settings.Object,
            Mock.Of<IQuotaEnforcer>(),
            NullLogger<InvitationService>.Instance);

        return (service, email, tenantUsers);
    }

    private static TenantContext Tenant(Guid? tenantId = null) => new()
    {
        TenantId = tenantId ?? TestTenantId,
        TenantSlug = "test",
        TenantDbConnectionString = "Host=localhost;Database=unused",
        Status = "active",
    };

    [Fact]
    public async Task Resend_PendingInvitation_RotatesToken_ResetsExpiry_AndEmailsANewLink()
    {
        var (id, originalHash, originalExpiry) = await SeedInvitationAsync(expiresInDays: 1);
        var (service, email, tenantUsers) = BuildService();

        var result = await service.ResendInvitationAsync(Tenant(), id, TestUserId);

        Assert.True(result);

        var (hash, expiry, _) = await ReadAsync(id);
        Assert.NotEqual(originalHash, hash);
        Assert.True(expiry > originalExpiry,
            $"expiry must be pushed forward: was {originalExpiry:o}, now {expiry:o}");

        // The mailed token must be the NEW one — only its hash is stored, so re-sending the
        // original is impossible by construction, and a leaked old link stops working.
        email.Verify(e => e.SendInvitationEmailAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        tenantUsers.Verify(t => t.RecordAuditEventAsync(
            It.IsAny<OrgContext>(), TenantAuditActions.InvitationResent, TestUserId,
            "invitation", id.ToString(), It.IsAny<object>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Resend_UnknownInvitation_ReturnsFalse_AndSendsNothing()
    {
        var (service, email, _) = BuildService();

        var result = await service.ResendInvitationAsync(Tenant(), Guid.NewGuid(), TestUserId);

        Assert.False(result);
        email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resend_AcceptedInvitation_ReturnsFalse_AndLeavesTheTokenSpent()
    {
        // An accepted invitation is spent. Resending must not mint a fresh usable token for it.
        var (id, originalHash, _) = await SeedInvitationAsync(accepted: true);
        var (service, email, _) = BuildService();

        var result = await service.ResendInvitationAsync(Tenant(), id, TestUserId);

        Assert.False(result);
        var (hash, _, _) = await ReadAsync(id);
        Assert.Equal(originalHash, hash);
        email.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Resend_InvitationOfAnotherTenant_ReturnsFalse_AndLeavesItUntouched()
    {
        // Cross-tenant isolation: the id is guessable, so tenant scoping in the WHERE is the
        // only thing standing between an admin of tenant A and a live invite link for tenant B.
        var (id, originalHash, _) = await SeedInvitationAsync();
        var (service, email, _) = BuildService();

        var result = await service.ResendInvitationAsync(Tenant(tenantId: Guid.NewGuid()), id, TestUserId);

        Assert.False(result);
        var (hash, _, _) = await ReadAsync(id);
        Assert.Equal(originalHash, hash);
        email.VerifyNoOtherCalls();
    }

    // ── seed / read helpers ───────────────────────────────────────────────────

    private async Task<(Guid Id, string TokenHash, DateTime ExpiresAt)> SeedInvitationAsync(
        bool accepted = false, int expiresInDays = 3)
    {
        var id = Guid.NewGuid();
        var tokenHash = $"seed-hash-{id:N}";
        var expiresAt = DateTime.UtcNow.AddDays(expiresInDays);

        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO invitations (id, email, role, invited_by, tenant_id, token_hash, expires_at, accepted_at)
            VALUES (@id, @email, 'editor', @invitedBy, @tenantId, @tokenHash, @expiresAt, @acceptedAt)", conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", $"resend-{id:N}@example.com");
        cmd.Parameters.AddWithValue("invitedBy", TestUserId);
        cmd.Parameters.AddWithValue("tenantId", TestTenantId);
        cmd.Parameters.AddWithValue("tokenHash", tokenHash);
        cmd.Parameters.AddWithValue("expiresAt", expiresAt);
        cmd.Parameters.AddWithValue("acceptedAt", accepted ? DateTime.UtcNow : (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();

        return (id, tokenHash, expiresAt);
    }

    private async Task<(string TokenHash, DateTime ExpiresAt, DateTime? AcceptedAt)> ReadAsync(Guid id)
    {
        await using var conn = new NpgsqlConnection(_connString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT token_hash, expires_at, accepted_at FROM invitations WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"invitation {id} vanished");
        return (reader.GetString(0), reader.GetDateTime(1), reader.IsDBNull(2) ? null : reader.GetDateTime(2));
    }
}

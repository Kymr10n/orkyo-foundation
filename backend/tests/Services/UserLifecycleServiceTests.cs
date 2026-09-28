using Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orkyo.Foundation.Tests.Mocks;

namespace Orkyo.Foundation.Tests.Services;

/// <summary>
/// Integration tests for the GDPR user lifecycle run. Exercise <see cref="UserLifecycleService.ProcessAsync"/>
/// against the test control-plane DB and assert that Keycloak calls go through the shared
/// <c>IKeycloakAdminService</c> (mocked) and emails through <c>IEmailService</c> (mocked) —
/// the service no longer maintains a private Keycloak client or SMTP stack.
/// </summary>
[Collection("Database collection")]
public class UserLifecycleServiceTests
{
    private readonly FoundationWebApplicationFactory _factory;
    private readonly MockKeycloakAdminService _mockKeycloak;
    private readonly MockEmailService _mockEmail;
    private readonly string _cpConnectionString;

    public UserLifecycleServiceTests(DatabaseFixture databaseFixture)
    {
        _factory = databaseFixture.Factory;
        _mockKeycloak = _factory.MockKeycloakAdminService;
        _mockKeycloak.Reset();
        _mockEmail = _factory.MockEmailService;
        _mockEmail.Reset();
        _cpConnectionString = databaseFixture.ControlPlaneConnectionString;
    }

    // ─── helpers ────────────────────────────────────────────────────────────────

    private async Task ProcessAsync()
    {
        // The service resolves IKeycloakAdminService / IEmailService from a scope internally;
        // in the test host those resolve to the shared mocks.
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<UserLifecycleService>();
        await service.ProcessAsync(CancellationToken.None);
    }

    /// <summary>
    /// Creates a control-plane user in a specific lifecycle state. Interval offsets are in days
    /// relative to NOW (positive = that many days in the past).
    /// </summary>
    private async Task<Guid> CreateLifecycleUserAsync(
        string? lifecycleStatus,
        int warningCount = 0,
        int? lastWarnedDaysAgo = null,
        int? dormantDaysAgo = null,
        string? keycloakId = null,
        int? lastLoginDaysAgo = null)
    {
        var email = $"ulc_{Guid.NewGuid()}@example.com";
        var userId = await DatabaseTestUtils.CreateTestUserAsync(
            email, displayName: "Lifecycle Service Test", tenantSlug: null, active: true);

        var warnedSql = lastWarnedDaysAgo is null ? "NULL" : "NOW() - make_interval(days => @warnedDaysAgo)";
        var dormantSql = dormantDaysAgo is null ? "NULL" : "NOW() - make_interval(days => @dormantDaysAgo)";
        var loginSql = lastLoginDaysAgo is null ? "NULL" : "NOW() - make_interval(days => @loginDaysAgo)";

        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($@"
            UPDATE users
            SET lifecycle_status         = @status,
                lifecycle_warning_count  = @count,
                lifecycle_last_warned_at = {warnedSql},
                lifecycle_dormant_since  = {dormantSql},
                last_login_at            = {loginSql}
            WHERE id = @id;
            INSERT INTO user_identities (user_id, provider, provider_subject)
            SELECT @id, 'keycloak', @kcId WHERE @kcId IS NOT NULL", conn);
        cmd.Parameters.AddWithValue("status", (object?)lifecycleStatus ?? DBNull.Value);
        cmd.Parameters.AddWithValue("count", warningCount);
        if (lastWarnedDaysAgo is not null) cmd.Parameters.AddWithValue("warnedDaysAgo", lastWarnedDaysAgo.Value);
        if (dormantDaysAgo is not null) cmd.Parameters.AddWithValue("dormantDaysAgo", dormantDaysAgo.Value);
        cmd.Parameters.Add(new NpgsqlParameter("kcId", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)keycloakId ?? DBNull.Value });
        if (lastLoginDaysAgo is not null) cmd.Parameters.AddWithValue("loginDaysAgo", lastLoginDaysAgo.Value);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();

        return userId;
    }

    private async Task<(bool exists, string? lifecycleStatus, int warningCount, string dbStatus)> GetUserStateAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT lifecycle_status, lifecycle_warning_count, status FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return (false, null, 0, "");
        return (true,
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.GetInt32(1),
            reader.GetString(2));
    }

    private async Task DeleteUserAsync(Guid userId)
    {
        await using var conn = new NpgsqlConnection(_cpConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM users WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ─── warning phase ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_InactiveUser_ReceivesFirstWarning()
    {
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: null, lastLoginDaysAgo: 400); // > 12 months
        try
        {
            await ProcessAsync();

            var (exists, status, count, _) = await GetUserStateAsync(userId);
            exists.Should().BeTrue();
            status.Should().Be("warned");
            count.Should().Be(1);
            _mockEmail.CallCount(nameof(IEmailService.SendLifecycleWarningEmailAsync)).Should().BeGreaterThanOrEqualTo(1);
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ProcessAsync_Warning_StoresOnlyTheTokenHashAndTheMailedTokenConfirms()
    {
        var userId = await CreateLifecycleUserAsync(lifecycleStatus: null, lastLoginDaysAgo: 400);
        try
        {
            await ProcessAsync();

            await using var conn = new NpgsqlConnection(_cpConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT email, lifecycle_confirm_token FROM users WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", userId);
            await using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            var mailed = _mockEmail.LifecycleWarningTokens[reader.GetString(0)];
            var stored = reader.GetString(1);

            mailed.Length.Should().BeGreaterThanOrEqualTo(43, "the token carries 256 bits");
            stored.Should().NotBe(mailed, "only the digest is stored");

            using var scope = _factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<Api.Repositories.IPlatformUserRepository>();
            var record = await repo.FindActiveLifecycleConfirmAsync(mailed);
            record.Should().NotBeNull();
            record!.UserId.Should().Be(userId);
            (await repo.FindActiveLifecycleConfirmAsync(stored)).Should().BeNull("the stored digest is not a token");
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ProcessAsync_WhenWarningSendFails_LeavesUserUnwarned()
    {
        var userId = await CreateLifecycleUserAsync(lifecycleStatus: null, lastLoginDaysAgo: 400);
        _mockEmail.FailLifecycleWarnings = true;
        try
        {
            await ProcessAsync();

            _mockEmail.CallCount(nameof(IEmailService.SendLifecycleWarningEmailAsync)).Should().BeGreaterThanOrEqualTo(1);
            // An unsent warning must not count: the user stays where they were for the next run.
            var (exists, status, count, _) = await GetUserStateAsync(userId);
            exists.Should().BeTrue();
            status.Should().BeNull();
            count.Should().Be(0);
        }
        finally
        {
            _mockEmail.FailLifecycleWarnings = false;
            await DeleteUserAsync(userId);
        }
    }

    // ─── deactivation phase ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_ThirdWarningTimedOut_DisablesViaKeycloakAdminServiceAndSendsDormancyNotice()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "warned", warningCount: 3, lastWarnedDaysAgo: 15, keycloakId: keycloakId);
        try
        {
            await ProcessAsync();

            _mockKeycloak.DisableUserCallCount.Should().Be(1);
            _mockKeycloak.LastDisabledKeycloakId.Should().Be(keycloakId);
            _mockEmail.CallCount(nameof(IEmailService.SendDormancyNoticeEmailAsync)).Should().Be(1);

            var (exists, status, _, dbStatus) = await GetUserStateAsync(userId);
            exists.Should().BeTrue();
            status.Should().Be("dormant");
            dbStatus.Should().Be("disabled");
        }
        finally
        {
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ProcessAsync_WhenKeycloakDisableFails_LeavesUserWarnedAndSendsNoNotice()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "warned", warningCount: 3, lastWarnedDaysAgo: 15, keycloakId: keycloakId);
        _mockKeycloak.DisableUserSuccess = false;
        _mockKeycloak.DisableUserError = "Keycloak temporarily unavailable";
        try
        {
            await ProcessAsync();

            _mockKeycloak.DisableUserCallCount.Should().Be(1);
            _mockEmail.CallCount(nameof(IEmailService.SendDormancyNoticeEmailAsync)).Should().Be(0);

            // A Keycloak failure must skip the user — state stays 'warned' for the next run.
            var (exists, status, count, dbStatus) = await GetUserStateAsync(userId);
            exists.Should().BeTrue();
            status.Should().Be("warned");
            count.Should().Be(3);
            dbStatus.Should().Be("active");
        }
        finally
        {
            _mockKeycloak.DisableUserSuccess = true;
            await DeleteUserAsync(userId);
        }
    }

    [Fact]
    public async Task ProcessAsync_WhenDormancyNoticeFails_LeavesUserWarned()
    {
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "warned", warningCount: 3, lastWarnedDaysAgo: 15, keycloakId: Guid.NewGuid().ToString());
        _mockEmail.FailDormancyNotices = true;
        try
        {
            await ProcessAsync();

            _mockEmail.CallCount(nameof(IEmailService.SendDormancyNoticeEmailAsync)).Should().BeGreaterThanOrEqualTo(1);
            // No notice, no dormancy: the purge clock must not start for a user who was not told.
            var (exists, status, count, dbStatus) = await GetUserStateAsync(userId);
            exists.Should().BeTrue();
            status.Should().Be("warned");
            count.Should().Be(3);
            dbStatus.Should().Be("active");
        }
        finally
        {
            _mockEmail.FailDormancyNotices = false;
            await DeleteUserAsync(userId);
        }
    }

    // ─── purge phase ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DormantPastRetention_DeletesFromKeycloakAndPurgesDbRow()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "dormant", warningCount: 3, dormantDaysAgo: 91, keycloakId: keycloakId);

        await ProcessAsync();

        _mockKeycloak.DeleteUserCallCount.Should().Be(1);
        _mockKeycloak.LastDeletedKeycloakId.Should().Be(keycloakId);

        var (exists, _, _, _) = await GetUserStateAsync(userId);
        exists.Should().BeFalse("the GDPR purge must remove the users row");
    }

    [Fact]
    public async Task ProcessAsync_WhenKeycloakDeleteFails_StillPurgesDbRow()
    {
        var keycloakId = Guid.NewGuid().ToString();
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "dormant", warningCount: 3, dormantDaysAgo: 91, keycloakId: keycloakId);
        _mockKeycloak.DeleteUserSuccess = false;
        _mockKeycloak.DeleteUserError = "Keycloak temporarily unavailable";
        try
        {
            await ProcessAsync();

            _mockKeycloak.DeleteUserCallCount.Should().Be(1);

            // The app-data purge proceeds even when Keycloak deletion fails (pre-existing behavior).
            var (exists, _, _, _) = await GetUserStateAsync(userId);
            exists.Should().BeFalse();
        }
        finally
        {
            _mockKeycloak.DeleteUserSuccess = true;
        }
    }

    [Fact]
    public async Task ProcessAsync_DormantUserWithoutKeycloakId_PurgesWithoutKeycloakCall()
    {
        var userId = await CreateLifecycleUserAsync(
            lifecycleStatus: "dormant", warningCount: 3, dormantDaysAgo: 91, keycloakId: null);

        await ProcessAsync();

        _mockKeycloak.DeleteUserCallCount.Should().Be(0);
        var (exists, _, _, _) = await GetUserStateAsync(userId);
        exists.Should().BeFalse();
    }
}
